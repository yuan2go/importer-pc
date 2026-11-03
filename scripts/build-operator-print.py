"""Build local, searchable A4 operator PDFs from the approved Markdown sources.

Optional authoring tool, never loaded by the application. See
docs/runtime/industrial-print-guides-01.md for dependencies and QA boundaries.
"""
from __future__ import annotations

import argparse
import hashlib
from html import escape, unescape
import importlib.metadata
import io
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont as SourceFont
from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib import textsplit
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import CondPageBreak, Flowable, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle
from reportlab.platypus import paragraph as paragraph_module

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ('quick-guide', 'deployment-card', 'implementation-guide', 'fault-codes')
INK, MUTED, LINE, ACCENT = map(colors.HexColor, ('#202D40', '#536278', '#D5DFEA', '#165DAD'))


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def register_font(path: Path, characters: str, name: str) -> None:
    # ReportLab cannot embed CFF outlines directly. Convert only required glyphs
    # to quadratic outlines in memory, retaining the approved OFL font's metrics.
    # This temporary subset is not a second distributed font asset.
    with SourceFont(path) as original:
        cmap = original.getBestCmap()
        points = sorted({ord(c) for c in characters if not c.isspace()} | {32})
        missing = [chr(c) for c in points if c not in cmap]
        if missing:
            raise ValueError('Approved font is missing required glyphs: ' + repr(missing))
        glyph_names = ['.notdef'] + sorted({cmap[c] for c in points})
        builder = FontBuilder(original['head'].unitsPerEm, isTTF=True)
        builder.setupGlyphOrder(glyph_names)
        builder.setupCharacterMap({c: cmap[c] for c in points})
        source_glyphs = original.getGlyphSet()
        glyphs = {}
        for glyph_name in glyph_names:
            pen = TTGlyphPen(None)
            source_glyphs[glyph_name].draw(Cu2QuPen(pen, max_err=1, reverse_direction=True))
            glyphs[glyph_name] = pen.glyph()
        builder.setupGlyf(glyphs)
        builder.setupHorizontalMetrics({g: original['hmtx'][g] for g in glyph_names})
        ascent, descent = original['hhea'].ascent, original['hhea'].descent
        builder.setupHorizontalHeader(ascent=ascent, descent=descent)
        builder.setupNameTable({'familyName': name, 'styleName': 'Regular', 'uniqueFontIdentifier': name,
                               'fullName': name, 'psName': name, 'version': 'Version 1.0'})
        builder.setupOS2(sTypoAscender=ascent, sTypoDescender=descent,
                        usWinAscent=original['OS/2'].usWinAscent, usWinDescent=original['OS/2'].usWinDescent)
        builder.setupPost()
        builder.font.recalcTimestamp = False
        builder.font['head'].created = builder.font['head'].modified = 2082844800
        buffer = io.BytesIO()
        builder.save(buffer)
        buffer.seek(0)
        pdfmetrics.registerFont(TTFont(name, buffer))


def markup(text: str) -> str:
    # Never interpret source HTML, images or arbitrary remote content.
    text = re.sub(r'\[([^\]]+)\]\([^)]+\)', r'\1', text)
    text = re.sub(r'`([^`]+)`', r'\1', text)
    parts = re.split(r'(\*\*.*?\*\*)', text)
    return ''.join('<b>' + escape(p[2:-2]) + '</b>' if p.startswith('**') else escape(p) for p in parts)


def styles(card: bool) -> dict:
    body = ParagraphStyle('body', fontName='IpcPrintRegular', fontSize=11.5 if card else 11,
                          leading=17 if card else 16, textColor=INK, wordWrap='CJK', alignment=TA_LEFT,
                          spaceAfter=9, allowWidows=0, allowOrphans=0)
    return {
        'body': body,
        'title': ParagraphStyle('title', parent=body, fontName='IpcPrintMedium', fontSize=24, leading=31, spaceAfter=14),
        'heading': ParagraphStyle('heading', parent=body, fontName='IpcPrintMedium', fontSize=17, leading=24,
                                 spaceBefore=12, spaceAfter=12, keepWithNext=True),
        'sub': ParagraphStyle('sub', parent=body, fontName='IpcPrintMedium', fontSize=13, leading=20,
                             spaceBefore=8, spaceAfter=9, keepWithNext=True),
        'note': ParagraphStyle('note', parent=body, fontSize=9.5, leading=14, textColor=MUTED),
        'table': ParagraphStyle('table', parent=body, fontSize=10, leading=15, spaceAfter=0),
    }


def verify_text(reader: PdfReader, sources: list[str]) -> int:
    rendered = re.sub(r'\s+', '', '\n'.join(page.extract_text() for page in reader.pages))
    if '\ufffd' in rendered:
        raise ValueError('Replacement glyph in searchable text')
    checked = 0
    for source in sources:
        for line in source.splitlines():
            line = line.strip()
            if not line or line.startswith('```'):
                continue
            cells = [c.strip() for c in line.strip('|').split('|')] if line.startswith('|') else [re.sub(r'^#{1,3} ', '', line)]
            if all(re.fullmatch(r':?-+:?', c) for c in cells):
                continue
            for cell in cells:
                expected = re.sub(r'\s+', '', unescape(markup(cell).replace('<b>', '').replace('</b>', '')))
                if expected not in rendered:
                    raise ValueError('Source text missing from PDF: ' + cell[:100])
                checked += 1
    embedded = set()
    for page in reader.pages:
        for reference in page['/Resources']['/Font'].values():
            font = reference.get_object()
            if font.get('/FontDescriptor') and font['/FontDescriptor'].get('/FontFile2'):
                embedded.add(str(font['/BaseFont']).split('+')[-1])
    if embedded != {'IpcPrintRegular', 'IpcPrintMedium'}:
        raise ValueError('Both approved outline subsets must be embedded: ' + repr(embedded))
    return checked


class PlacementDiagram(Flowable):
    """Generic schematic only: no customer screenshot, example chute or real barcode."""
    def __init__(self) -> None:
        super().__init__()
        self.width, self.height = A4[0] - 88, 78

    def draw(self) -> None:
        canvas = self.canv
        for index, (title, detail) in enumerate((('左位', '只看左位本件格口'), ('右位', '只看右位本件格口'))):
            x, width = index * (self.width + 12) / 2, (self.width - 12) / 2
            canvas.setStrokeColor(LINE)
            canvas.roundRect(x, 9, width, 60, 5, stroke=1, fill=0)
            canvas.setFillColor(ACCENT)
            canvas.setFont('IpcPrintMedium', 13)
            canvas.drawString(x + 14, 48, title)
            canvas.setFillColor(INK)
            canvas.setFont('IpcPrintRegular', 11)
            canvas.drawString(x + 14, 27, detail)


class PrintDocument(SimpleDocTemplate):
    def afterFlowable(self, flowable: Flowable) -> None:
        if isinstance(flowable, Paragraph) and flowable.style.name == 'heading':
            title = flowable.getPlainText()
            self.canv.bookmarkPage(title)
            self.canv.addOutlineEntry(title, title, level=0)


def content(source: str, card: bool) -> list:
    style = styles(card)
    result, pending, table, in_code = [], [], [], False

    def paragraph() -> None:
        if pending:
            result.append(Paragraph(markup(' '.join(pending)), style['body']))
            pending.clear()

    def table_rows() -> None:
        if not table:
            return
        cells = [[Paragraph(markup(cell), style['table']) for cell in row] for row in table]
        width = A4[0] - 88
        # Four-column fault catalogue; three-column installer catalogue.
        ratios = [0.19, 0.24, 0.28, 0.29] if len(cells[0]) == 4 else [0.24, 0.31, 0.45]
        if any(len(row) != len(cells[0]) for row in cells):
            raise ValueError('Inconsistent source table')
        block = Table(cells, colWidths=[width * r for r in ratios], repeatRows=1, hAlign='LEFT')
        block.setStyle(TableStyle([
            ('VALIGN', (0, 0), (-1, -1), 'TOP'), ('BOX', (0, 0), (-1, -1), .5, LINE),
            ('INNERGRID', (0, 0), (-1, -1), .4, LINE), ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#EEF3F9')),
            ('LEFTPADDING', (0, 0), (-1, -1), 8), ('RIGHTPADDING', (0, 0), (-1, -1), 8),
            ('TOPPADDING', (0, 0), (-1, -1), 8), ('BOTTOMPADDING', (0, 0), (-1, -1), 8),
        ]))
        result.extend([block, Spacer(1, 12)])
        table.clear()

    for line in source.splitlines() + ['']:
        stripped = line.strip()
        if stripped.startswith('```'):
            paragraph()
            in_code = not in_code
            continue
        if stripped.startswith('|') and not in_code:
            paragraph()
            cells = [c.strip() for c in stripped.strip('|').split('|')]
            if not all(re.fullmatch(r':?-+:?', c) for c in cells):
                table.append(cells)
            continue
        table_rows()
        if not stripped:
            paragraph()
        elif stripped.startswith('# '):
            paragraph()
            result.append(Paragraph(markup(stripped[2:]), style['title']))
            if card and '供包员' in stripped:
                result.append(PlacementDiagram())
        elif stripped.startswith('## '):
            paragraph()
            if not card:
                result.append(CondPageBreak(100))
            result.append(Paragraph(markup(stripped[3:]), style['heading']))
        elif stripped.startswith('### '):
            paragraph()
            result.append(Paragraph(markup(stripped[4:]), style['sub']))
        elif re.match(r'^\d+\. ', stripped):
            paragraph()
            result.append(Paragraph(markup(stripped), style['body']))
        elif in_code:
            paragraph()
            result.append(Paragraph(escape(stripped), style['note']))
        else:
            pending.append(stripped)
    if in_code:
        raise ValueError('Unclosed source code block')
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New directory below repository .artifacts')
    args = parser.parse_args()
    output = args.output.absolute()
    if not output.resolve().is_relative_to(ROOT / '.artifacts'):
        raise ValueError('Output must be within repository .artifacts')
    if any(p.is_symlink() or bool(getattr(p.lstat(), 'st_file_attributes', 0) & 0x400)
           for p in [output, *output.parents] if p.exists()):
        raise ValueError('Output ancestors cannot be links/reparse points')
    output.mkdir(parents=True, exist_ok=False)
    # ReportLab's Japanese-oriented defaults omit simplified Chinese punctuation.
    # Extend the two local process line breakers; never alter installed libraries.
    forbidden_start = textsplit.ALL_CANNOT_START + '，；：！？）》】”’％'
    textsplit.ALL_CANNOT_START = forbidden_start
    paragraph_module.ALL_CANNOT_START = forbidden_start
    texts = {name: (ROOT / 'docs/operator' / (name + '.md')).read_text(encoding='utf-8') for name in SOURCES}
    version_file = ROOT / 'src/Ipc.Application.Sportmaster/Ipc.Application.Sportmaster.csproj'
    version = ET.parse(version_file).findtext('./PropertyGroup/Version')
    if not version or not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Expected the actual candidate business version')
    if any(version not in texts[name].splitlines()[2] for name in ('deployment-card', 'implementation-guide')):
        raise ValueError('Operator source candidate versions do not match the project')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], cwd=ROOT, text=True).strip())
    footer_text = 'SM ' + version + ' IE01候选 | 源 ' + commit[:12] + (' +工作区' if dirty else '')
    corpus = ''.join(texts.values()) + footer_text + '0123456789 / 页左位右位只看本件格口'
    fonts = ROOT / 'src/Ipc.Presentation.Wpf/Fonts'
    for suffix in ('Regular', 'Medium'):
        register_font(fonts / ('NotoSansCJKsc-' + suffix + '.otf'), corpus, 'IpcPrint' + suffix)
    pdfmetrics.registerFontFamily('IpcPrintRegular', normal='IpcPrintRegular', bold='IpcPrintMedium',
                                 italic='IpcPrintRegular', boldItalic='IpcPrintMedium')

    def footer(canvas: Canvas, document: SimpleDocTemplate) -> None:
        canvas.saveState()
        canvas.setStrokeColor(LINE)
        canvas.line(44, 41, A4[0] - 44, 41)
        canvas.setFillColor(MUTED)
        canvas.setFont('IpcPrintRegular', 8)
        canvas.drawString(44, 27, footer_text)
        canvas.drawRightString(A4[0] - 44, 27, str(document.page))
        canvas.restoreState()

    outputs = {}
    for name in SOURCES[:3]:
        path = output / (name + '.pdf')
        story = content(texts[name], card=name != 'implementation-guide')
        if name == 'implementation-guide':
            story.extend([CondPageBreak(250), Spacer(1, 18), *content(texts['fault-codes'], card=False)])
        document = PrintDocument(str(path), pagesize=A4, leftMargin=44, rightMargin=44,
                                     topMargin=40, bottomMargin=58, title=texts[name].splitlines()[0][2:],
                                     author='ImporterPc', pageCompression=1, invariant=1)
        document.build(story, onFirstPage=footer, onLaterPages=footer)
        reader = PdfReader(path)
        pages = len(reader.pages)
        if pages < 1 or pages > (20 if name == 'implementation-guide' else 1):
            raise ValueError('Page budget exceeded: ' + name + ' pages=' + str(pages))
        checked = verify_text(reader, [texts[name], texts['fault-codes']] if name == 'implementation-guide' else [texts[name]])
        outputs[path.name] = {'pages': pages, 'bytes': path.stat().st_size, 'sha256': digest(path),
                              'sourceTextSegmentsVerified': checked, 'bothFontSubsetsEmbedded': True}
        print('BUILT ' + path.name + ' pages=' + str(pages))
    source_files = [ROOT / 'docs/operator' / (name + '.md') for name in SOURCES]
    source_files += [Path(__file__), ROOT / 'scripts/operator-print.requirements.txt', version_file,
                     fonts / 'NotoSansCJKsc-Regular.otf', fonts / 'NotoSansCJKsc-Medium.otf', fonts / 'OFL.txt']
    manifest = {'schema': 1, 'sourceCommit': commit, 'sourceWorktreeDirty': dirty, 'candidateOnly': True,
                'fixedMetadataTimestampsForReproducibility': True,
                'physicalPrintingVerified': False, 'newUserJourneyVerified': False,
                'visualReview': 'RequiredSeparately', 'outputs': outputs,
                'sources': {str(p.relative_to(ROOT)).replace('\\', '/'): digest(p) for p in source_files},
                'python': sys.version.split()[0],
                'tools': {name: importlib.metadata.version(name) for name in ('reportlab', 'fonttools', 'pypdf', 'pillow', 'charset-normalizer')}}
    (output / 'print-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (output / 'OFL.txt').write_bytes((fonts / 'OFL.txt').read_bytes())


if __name__ == '__main__':
    main()
