"""Check current operator entry links and all runtime CP fault codes, not archives."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def package_index():
    paths = sorted((ROOT / 'docs/archive/work-packages').glob('WP-IPC-*.md'))
    return ('# 工作包目录\n\n由`scripts/verify-industrial-docs.py --generate-work-packages`生成；'
            '只导航，不重写各包的原身份与状态。\n\n'
            + ''.join(f'- [{p.stem}]({p.name})\n' for p in paths))


def verify():
    files = [ROOT / 'README.md', ROOT / 'CHANGELOG.md', ROOT / 'docs/INDEX.md',
             ROOT / 'docs/archive/work-packages/README.md', *sorted((ROOT / 'docs/operator').glob('*.md'))]
    errors = []
    count = 0
    for path in files:
        text = path.read_text(encoding='utf-8-sig')
        for target in re.findall(r'\]\(([^)]+)\)', text):
            if target.startswith(('https://', 'http://', '#', 'mailto:')):
                continue
            count += 1
            if not (path.parent / target.split('#')[0]).exists():
                errors.append(f'{path.relative_to(ROOT)}: missing {target}')
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
    if (ROOT / 'docs/archive/work-packages/README.md').read_text(encoding='utf-8') != package_index():
        errors.append('Work package index is stale; generate it explicitly')
    if errors:
        raise ValueError('\n'.join(errors))
    print(f'PASS DOCS: {len(files)} entry pages, {count} local links, {len(codes)} CP codes; printing/native/site NOT_RUN')


if __name__ == '__main__':
    if sys.argv[1:] == ['--generate-work-packages']:
        (ROOT / 'docs/archive/work-packages/README.md').write_text(package_index(), encoding='utf-8')
    elif sys.argv[1:]:
        raise SystemExit('Usage: verify-industrial-docs.py [--generate-work-packages]')
    verify()
