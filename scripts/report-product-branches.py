"""Report real OpenCover branch points and inventory every owned production source.

An uncovered branch is not labelled unreachable. Missing instrumentation has unknown
branch count; it is never treated as zero or removed to obtain a whole-product rate.
Assemblies with different bytes are separate build variants, not max-merged coverage.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def analyze(root: Path, results: Path):
    inventory = {p.relative_to(root).as_posix(): {'sha256': sha(p), 'branches': {}, 'measured': False}
                 for p in sorted((root / 'src').rglob('*.cs')) if not {'bin', 'obj'}.intersection(p.relative_to(root).parts)}
    reports = sorted(results.glob('*.opencover.xml'))
    if not reports:
        raise ValueError('No actual OpenCover report')
    raw = []
    variants = set()
    unmapped = {}
    for report in reports:
        tree = ET.parse(report).getroot()
        if tree.tag != 'CoverageSession':
            raise ValueError('Expected actual branch collector output')
        data_path = report.with_name(report.name.replace('.opencover.xml', '.json'))
        manifest_path = report.with_name(report.name.replace('.opencover.xml', '.modules.json'))
        # OpenCoverReporter skips methods without sequence lines, although its
        # summary includes their real branches. Consume the companion collector
        # JSON so those paths cannot disappear from our denominator.
        data = json.loads(data_path.read_text(encoding='utf-8-sig'))
        manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
        identities = {m['name']: m['sha256'].lower() for m in manifest}
        raw.append({'file': report.name, 'sha256': sha(report), 'branchData': data_path.name, 'branchDataSha256': sha(data_path), 'assemblyManifestSha256': sha(manifest_path)})
        raw_count = 0
        for module, documents in data.items():
            name = Path(module).stem
            if not name.startswith('Ipc.') or name.endswith('.Verification') or name == 'Ipc.Core.Tests':
                raise ValueError('Non-product module in measurement: ' + name)
            identity = identities.get(name)
            if not identity or len(identity) != 64 or any(c not in '0123456789abcdef' for c in identity):
                raise ValueError('Missing original assembly SHA256: ' + name)
            variants.add((name, identity))
            for path, classes in documents.items():
                normalized = path.replace('\\', '/')
                source = 'src/' + normalized.rsplit('/src/', 1)[1] if '/src/' in normalized else None
                if source not in inventory:
                    source = None
                for classname, methods in classes.items():
                    for method_name, method in methods.items():
                        if source and (method['Lines'] or method['Branches']):
                            inventory[source]['measured'] = True
                        for point in method['Branches']:
                            raw_count += 1
                            number, hits = int(point['Line']), int(point['Hits'])
                            if number < 0 or hits < 0:
                                raise ValueError('Invalid collector branch record')
                            key = '|'.join(map(str, [name, identity, normalized, classname, method_name, point['Offset'], point['Path'], point['EndOffset'], point['Ordinal'], number]))
                            points = inventory[source]['branches'] if source else unmapped
                            existing = points.get(key)
                            if source:
                                inventory[source]['measured'] = True
                            points[key] = {'line': number or None, 'hits': hits + (existing['hits'] if existing else 0), 'module': name, 'variant': identity, 'method': method_name, 'offset': point['Offset'], 'path': point['Path']}
        if raw_count != int(tree.find('Summary').attrib['numBranchPoints']):
            raise ValueError('Companion JSON branch records disagree with the original OpenCover summary')
    rows = []
    for source, item in inventory.items():
        branches = list(item['branches'].values())
        covered = sum(b['hits'] > 0 for b in branches)
        status = 'measured' if item['measured'] else 'no_instrumented_points'
        owner = source.split('/')[1]
        rows.append({'source': source, 'sha256': item['sha256'], 'status': status, 'knownBranches': len(branches) if item['measured'] else None, 'coveredBranches': covered if item['measured'] else None,
                     'uncovered': [b for b in branches if not b['hits']],
                     'limitation': None if item['measured'] else f'No sequence/branch points from {owner} for this source. Its composition may be absent, linked only into an excluded test wrapper, or contain no executable points. Reachability has not been proved; instrument/inspect the actual owning assembly. Hardware/privilege/paused-module qualification requires separate evidence.'})
    total = sum(len(item['branches']) for item in inventory.values()) + len(unmapped)
    covered = sum(b['hits'] > 0 for item in inventory.values() for b in item['branches'].values()) + sum(b['hits'] > 0 for b in unmapped.values())
    if not total or not covered:
        raise ValueError('Empty/all-zero actual branch evidence')
    summary = {'schema': 1, 'wholeProductBranchCoverage': False, 'scope': 'actual branch paths, module byte identity and method IL offset/path; all owned src C# inventoried',
               'measuredBranches': total, 'coveredBranches': covered, 'measuredBranchPercent': round(covered * 100 / total, 4),
               'unmappedCompilerOrGeneratedBranches': list(unmapped.values()), 'productionFiles': len(rows), 'unmeasuredFiles': sum(r['status'] != 'measured' for r in rows), 'variants': sorted(variants), 'rawReports': raw, 'files': rows}
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True); parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.root.resolve(), args.results.resolve())
    executions_file = args.results / 'executions.json'
    if executions_file.exists():
        executions = json.loads(executions_file.read_text(encoding='utf-8-sig'))
        result['executions'] = executions if isinstance(executions, list) else [executions]
        result['regressionsPassed'] = all(e['exitCode'] == 0 for e in result['executions'])
    source_file = args.results / 'source.json'
    if source_file.exists():
        result['sourceState'] = json.loads(source_file.read_text(encoding='utf-8-sig'))
        if result['sourceState'].get('sourceInputsChanged', False):
            result['regressionsPassed'] = False
    trx = args.results / 'tests/core.trx'
    if trx.exists():
        counters = ET.parse(trx).getroot().find('./{*}ResultSummary/{*}Counters')
        if counters is None:
            raise ValueError('Missing original test execution counters')
        result['coreTests'] = dict(counters.attrib)
        complete = int(counters.attrib['executed']) == int(counters.attrib['total']) and int(counters.attrib['passed']) == int(counters.attrib['total'])
        result['regressionsPassed'] = result.get('regressionsPassed', False) and complete

    (args.results / 'branches.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    lines = ['# Actual product branch measurements', '', '**Whole-product/full reachable-branch coverage has not been established.**', '', f"Regression execution passed: {result.get('regressionsPassed', 'not supplied')}.", '',
             f"Measured variants: {result['coveredBranches']} / {result['measuredBranches']} paths ({result['measuredBranchPercent']}%).",
             f"Owned production source inventory: {result['productionFiles']} files; {result['unmeasuredFiles']} without an established branch denominator.", '',
             'All compiler branch records come from companion collector JSON, including methods omitted from OpenCover detail because they have no sequence lines. Uncovered paths are not assumed unreachable. Native vendor internals, hardware, customer RCS and field qualification remain unrun.', '',
             '| Source | Measured branches | Covered | Status |', '|---|---:|---:|---|']
    lines += [f"| {r['source']} | {r['knownBranches'] if r['knownBranches'] is not None else 'unknown'} | {r['coveredBranches'] if r['coveredBranches'] is not None else 'unknown'} | {r['status']} |" for r in result['files']]
    (args.results / 'branches.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(f"Actual branches: {result['coveredBranches']}/{result['measuredBranches']}; unknown files={result['unmeasuredFiles']}; whole-product=False")


if __name__ == '__main__':
    main()
