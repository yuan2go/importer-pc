"""Validate the actual collector output; test wrappers never count as product coverage."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MODULES = ('Ipc.Platform', 'Ipc.Application.Contracts', 'Ipc.Application.ParcelFlow', 'Ipc.Application.Sportmaster')
REQUIRED_FILES = (
    'src/Ipc.Platform/LocalManagementMode.PasswordRecovery.cs',
    'src/Ipc.Platform/DeploymentRollback.cs',
    'src/Ipc.Platform/DeploymentQuietJournal.cs',
    'src/Ipc.Platform/QuietDeploymentOutput.cs',
    'src/Ipc.Application.ParcelFlow/ParcelRuntime.cs',
    'src/Ipc.Application.ParcelFlow/StationDiagnosticPackage.cs',
)
MAX_REPORT_BYTES = 32 * 1024 * 1024


def is_link(path: Path) -> bool:
    value = path.lstat()
    return stat.S_ISLNK(value.st_mode) or bool(getattr(value, 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0))


def source_name(value: str, module: str) -> str:
    normalized = '/' + value.replace('\\', '/').lstrip('/')
    marker = '/src/'
    if marker not in normalized:
        raise ValueError('Covered line has no repository product source path')
    result = 'src/' + normalized.rsplit(marker, 1)[1]
    if not result.startswith('src/' + module + '/') or not result.endswith('.cs') or '..' in result.split('/'):
        raise ValueError('Module/source identity mismatch')
    return result


def analyze(path: Path) -> dict:
    if is_link(path) or path.stat().st_size > MAX_REPORT_BYTES:
        raise ValueError('Coverage report is a link or exceeds its read budget')
    root = ET.parse(path).getroot()
    if root.tag != 'coverage':
        raise ValueError('Expected actual Cobertura output')
    lines: dict[tuple[str, int], int] = {}
    modules: dict[str, dict[tuple[str, int], int]] = {}
    for package in root.findall('./packages/package'):
        name = package.get('name', '')
        if name not in MODULES:
            raise ValueError('Unexpected/test module in product-only report: ' + name)
        own = modules.setdefault(name, {})
        for item in package.findall('./classes/class'):
            measured = item.findall('./lines/line')
            if not measured:
                continue
            filename = source_name(item.get('filename', ''), name)
            for line in measured:
                number, hits = int(line.get('number', '0')), int(line.get('hits', '-1'))
                if number <= 0 or hits < 0:
                    raise ValueError('Invalid executable-line measurement')
                key = filename, number
                own[key] = max(own.get(key, 0), hits)
                lines[key] = max(lines.get(key, 0), hits)
    if set(modules) != set(MODULES):
        raise ValueError('Missing expected product modules; child-process collection may have failed')

    def metrics(values: dict) -> dict:
        valid = len(values)
        covered = sum(hit > 0 for hit in values.values())
        if not valid or not covered:
            raise ValueError('Product coverage is empty or all-zero')
        return {'covered': covered, 'valid': valid, 'percent': round(100 * covered / valid, 4)}

    critical = {}
    for filename in REQUIRED_FILES:
        values = {key: hits for key, hits in lines.items() if key[0] == filename}
        critical[filename] = metrics(values)
    return {
        'schema': 1, 'scope': 'four managed product assemblies; unique source-file/line pairs',
        'wholeProductCoverage': False, 'testAssembliesIncluded': False,
        'modules': {name: metrics(modules[name]) for name in MODULES},
        'total': metrics(lines), 'requiredFiles': critical,
        'excluded': ['Assembly orchestration', 'WPF', 'native/vendor SDK', 'Windows system lifecycle and site acceptance'],
    }


def choose_report(results: Path) -> tuple[Path, str, int]:
    candidates, pending, entries = [], [results], 0
    while pending:
        directory = pending.pop()
        with os.scandir(directory) as items:
            for item in items:
                entries += 1
                if entries > 4096:
                    raise ValueError('Coverage results exceed enumeration budget')
                path = Path(item.path)
                if is_link(path):
                    raise ValueError('Coverage results contain a link/reparse entry')
                if item.is_dir(follow_symlinks=False):
                    if path != results / 'product-coverage':
                        pending.append(path)
                elif item.name.endswith('.cobertura.xml'):
                    candidates.append(path)
                    if len(candidates) > 16:
                        raise ValueError('Excessive collector reports')
    if not candidates or len(candidates) > 16:
        raise ValueError('Missing or excessive collector reports')
    unique = {}
    for candidate in candidates:
        if is_link(candidate) or candidate.stat().st_size > MAX_REPORT_BYTES:
            raise ValueError('Coverage report is a link or exceeds its read budget')
        digest = hashlib.sha256(candidate.read_bytes()).hexdigest()
        unique.setdefault(digest, candidate)
    if len(unique) != 1:
        raise ValueError('Different coverage sessions were mixed; use a fresh results directory')
    digest, path = next(iter(unique.items()))
    return path, digest, len(candidates)


def passed_test_count(results: Path) -> int:
    trx = results / 'core.trx'
    if is_link(trx) or trx.stat().st_size > 16 * 1024 * 1024:
        raise ValueError('Invalid or excessive original TRX')
    root = ET.parse(trx).getroot()
    summary = root.find('./{*}ResultSummary')
    counters = None if summary is None else summary.find('./{*}Counters')
    if summary is None or counters is None:
        raise ValueError('Original TRX has no execution counters')
    count = int(counters.get('executed', '0'))
    rows = root.findall('./{*}Results/{*}UnitTestResult')
    if (summary.get('outcome') != 'Completed' or count <= 0 or int(counters.get('total', '0')) != count or
            int(counters.get('passed', '0')) != count or int(counters.get('failed', '-1')) != 0 or
            int(counters.get('notExecuted', '-1')) != 0 or len(rows) != count or any(r.get('outcome') != 'Passed' for r in rows)):
        raise ValueError('Coverage cannot qualify missing, skipped or failed core tests')
    return count


def markdown(summary: dict) -> str:
    text = ['## Managed product coverage', '',
            '| Module | Covered / executable unique source lines | Coverage |', '|---|---:|---:|']
    for name, value in summary['modules'].items():
        text.append(f"| {name} | {value['covered']} / {value['valid']} | {value['percent']:.2f}% |")
    value = summary['total']
    text += [f"| Scope total | {value['covered']} / {value['valid']} | {value['percent']:.2f}% |", '',
             'Source file + line number are deduplicated across classes and async/record mappings.',
             'Four managed assemblies only; test wrappers are excluded. This is not whole-product coverage.',
             'Assembly orchestration, WPF, native/vendor SDK, Windows lifecycle and site acceptance are outside this measurement.',
             'All six required security/recovery/diagnostic source files contain executed product lines.',
             'No arbitrary percentage target is used for this initial baseline; empty, missing and all-zero product results fail.', '']
    return '\n'.join(text)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    results = args.results.resolve()
    if not results.is_relative_to(ROOT / '.artifacts') or not results.is_dir() or is_link(args.results.absolute()):
        raise ValueError('Expected the current test results directory')
    count = passed_test_count(results)
    report, digest, copies = choose_report(results)
    summary = analyze(report)
    summary['sourceCommit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    summary['collectorReportSha256'] = digest
    summary['identicalCollectorCopies'] = copies
    summary['passedCoreTests'] = count
    summary['settingsSha256'] = hashlib.sha256((ROOT / 'scripts/industrial-coverage.runsettings').read_bytes()).hexdigest()
    output = results / 'product-coverage'
    output.mkdir(exist_ok=True)
    if is_link(output):
        raise ValueError('Coverage output directory is a link')
    canonical = output / 'product.cobertura.xml'
    if canonical.exists() and hashlib.sha256(canonical.read_bytes()).hexdigest() != digest:
        raise ValueError('Refusing to overwrite a different source coverage report')
    if not canonical.exists():
        shutil.copyfile(report, canonical)
    for name in ('summary.json', 'summary.md'):
        if (output / name).exists() and is_link(output / name):
            raise ValueError('Coverage summary output is a link')
    (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    (output / 'summary.md').write_text(markdown(summary), encoding='utf-8')
    total = summary['total']
    print(f"PASS COVERAGE: 4 product modules; {total['covered']}/{total['valid']} unique source lines; "
          f"{total['percent']:.2f}%; six required files executed; wrappers/Windows/SDK qualification excluded")


if __name__ == '__main__':
    main()
