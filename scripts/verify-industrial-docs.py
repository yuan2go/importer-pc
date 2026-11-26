"""Check the active document index, repository links, delivery inputs and CP fault codes."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from urllib.parse import unquote, urlsplit

from release_documents import ALL_GUIDES

ROOT = Path(__file__).resolve().parents[1]


def package_index():
    paths = sorted((ROOT / 'docs/archive/work-packages').glob('WP-IPC-*.md'))
    return ('# 工作包目录\n\n由`scripts/verify-industrial-docs.py --generate-work-packages`生成；'
            '只导航，不重写各包的原身份与状态。\n\n'
            + ''.join(f'- [{p.stem}]({p.name})\n' for p in paths))


def verify():
    docs = ROOT / 'docs'
    index = docs / 'INDEX.md'
    active = {path for path in docs.rglob('*')
              if path.relative_to(docs).parts[0] != 'archive' and path.is_file()}
    files = [ROOT / name for name in ('AGENTS.md', 'README.md', 'CHANGELOG.md')]
    files.extend(sorted(path for path in active if path.suffix.lower() == '.md'))
    errors = []
    indexed = set()
    count = 0
    for path in files:
        text = path.read_text(encoding='utf-8-sig')
        for target in re.findall(r'\]\(([^)\n]+)\)', text):
            url = urlsplit(target.strip('<>'))
            if url.scheme or url.netloc or not url.path:
                continue
            destination = (path.parent / unquote(url.path)).resolve()
            count += 1
            if not destination.is_relative_to(ROOT) or not destination.exists():
                errors.append(f'{path.relative_to(ROOT)}: missing or outside repository: {target}')
            if path == index:
                indexed.add(destination)
    for path in sorted(active - indexed - {index}):
        errors.append(f'Not linked from docs/INDEX.md: {path.relative_to(ROOT)}')
    for source in sorted(ALL_GUIDES):
        if not (ROOT / source).is_file():
            errors.append(f'Missing packaged guide: {source}')
    codes = set()
    for path in (ROOT / 'src').rglob('*.cs'):
        if 'obj' not in path.parts and 'bin' not in path.parts:
            codes.update(re.findall(r'(?<![A-Za-z])CP-[A-Z][A-Z0-9-]*', path.read_text(encoding='utf-8-sig')))
    manual = (ROOT / 'docs/operator/fault-codes.md').read_text(encoding='utf-8')
    for code in sorted(codes):
        if f'| {code} |' not in manual:
            errors.append(f'No fault manual row for {code}')
    version = ET.parse(ROOT / 'src/Ipc.Application.Sportmaster/Ipc.Application.Sportmaster.csproj').findtext('./PropertyGroup/Version')
    if f'SM {version}' not in (ROOT / 'docs/operator/implementation-guide.md').read_text(encoding='utf-8'):
        errors.append('Operator manual version differs from actual SM project')
    if errors:
        raise ValueError('\n'.join(errors))
    print(f'PASS DOCS: {len(active) - 1} indexed files, {len(files)} Markdown pages, {count} local links, {len(ALL_GUIDES)} delivery sources, {len(codes)} CP codes')


if __name__ == '__main__':
    if sys.argv[1:] == ['--generate-work-packages']:
        (ROOT / 'docs/archive/work-packages/README.md').write_text(package_index(), encoding='utf-8')
    elif sys.argv[1:]:
        raise SystemExit('Usage: verify-industrial-docs.py [--generate-work-packages]')
    verify()
