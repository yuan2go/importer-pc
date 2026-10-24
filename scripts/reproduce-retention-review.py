"""Verify the immutable retention review, or reproduce it in a fresh Windows D: directory."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from xml.sax.saxutils import escape

BASELINE = '12a7cbdc11839dfae7eec88f9514e8559694bdd0'
ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'docs/runtime/evidence/retention-performance-review-01'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_originals(evidence):
    original = evidence / 'original'
    expected = {}
    for line in (original / 'SHA256SUMS').read_text(encoding='utf-8-sig').splitlines():
        digest, name = line.split('  ', 1)
        if Path(name).name != name or name in expected or len(digest) != 64:
            raise ValueError('Invalid original inventory entry: ' + name)
        expected[name] = digest.lower()
    actual = {p.name for p in original.iterdir() if p.is_file()}
    if actual != set(expected) | {'SHA256SUMS'}:
        raise ValueError('Original file set differs from the captured inventory')
    for name, digest in expected.items():
        if sha(original / name) != digest:
            raise ValueError('Original SHA256 mismatch: ' + name)
    rows = json.loads((evidence / 'FILES.json').read_text(encoding='utf-8'))
    if {r['path'] for r in rows} != {'original/' + name for name in actual} or len(rows) != len(actual):
        raise ValueError('Archive inventory does not cover the exact original file set')
    for row in rows:
        path = evidence / row['path']
        if row['sha256'] != sha(path) or row['bytes'] != path.stat().st_size:
            raise ValueError('Archive inventory mismatch: ' + row['path'])
    return len(actual)


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True, encoding='utf-8').strip()


def utc():
    return datetime.now(timezone.utc).isoformat()


def reproduce(output):
    if sys.platform != 'win32':
        raise ValueError('Native SQLite reproduction requires Windows; --verify-only is portable')
    if output.drive.lower() != 'd:' or output.exists() or output.is_relative_to(ROOT):
        raise ValueError('Use a new D: output directory outside the repository')
    for name in ('TEMP', 'TMP', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH'):
        if Path(os.environ.get(name, '')).drive.lower() != 'd:':
            raise ValueError('First dot-source scripts/project-environment.ps1: ' + name)
    scopes = ['src', 'tests', 'config', 'global.json', 'Directory.Build.props',
              'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config']
    git('diff', '--exit-code', BASELINE, '--', *scopes)
    if git('ls-files', '--others', '--exclude-standard', '--', 'src', 'tests', 'config'):
        raise ValueError('Untracked product inputs prevent fixed-source reproduction')
    record = dict(OriginalSourceCommit=BASELINE, ReproducedSourceCommit=git('rev-parse', 'HEAD'),
                  ReproducedSourceTree=git('rev-parse', 'HEAD^{tree}'),
                  ProductInputsMatchOriginal=True, OriginalEvidence=str(EVIDENCE.relative_to(ROOT)),
                  StartedUtc=utc(), Runs=[], Status='InProgress',
                  Qualification='Synthetic short tests only; no protected CLI, full expiry RTO, 24h/72h or field acceptance')
    output.mkdir(parents=True)

    def save():
        (output / 'REPRODUCTION.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')

    def run(name, command):
        entry = dict(Name=name, Command=list(map(str, command)), StartedUtc=utc(), Log=name + '.log')
        record['Runs'].append(entry)
        started = time.perf_counter()
        with (output / entry['Log']).open('wb') as log:
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        entry.update(EndedUtc=utc(), Seconds=time.perf_counter() - started, ExitCode=result.returncode)
        save()
        if result.returncode:
            raise RuntimeError(name + ' failed; preserve ' + str(output / entry['Log']))
        print('PASS ' + name, flush=True)

    try:
        original = EVIDENCE / 'original'
        run('build', ['dotnet', 'build', 'tests/Ipc.Application.Verification', '-c', 'Release', '-p:RestoreLockedMode=true'])
        query = output / 'query'
        query.mkdir()
        shutil.copyfile(original / 'Program.cs', query / 'Program.cs')
        run('extract-query', [sys.executable, str(original / 'prepare_probe.py'), str(ROOT), str(query)])
        run('query', ['dotnet', 'run', '--project', str(query / 'Probe.csproj'), '-c', 'Release', '--', str(query)])
        migration = output / 'migration'
        migration.mkdir()
        shutil.copyfile(original / 'MigrationProgram.cs', migration / 'MigrationProgram.cs')
        project = ROOT / 'src/Ipc.Application.ParcelFlow/Ipc.Application.ParcelFlow.csproj'
        (migration / 'MigrationProbe.csproj').write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
            '<Compile Include="MigrationProgram.cs"/><ProjectReference Include="' + escape(str(project), {'"': '&quot;'}) +
            '"/></ItemGroup></Project>', encoding='utf-8')
        run('migration', ['dotnet', 'run', '--project', str(migration / 'MigrationProbe.csproj'), '-c', 'Release', '--', str(migration)])
        verification = ROOT / 'tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll'
        for case in ('win11-stability', 'upgrade-conflict-closure', 'audit03-maintenance', 'version-compatibility'):
            run(case, ['dotnet', str(verification), str(ROOT), '--' + case])
        record['Status'] = 'PASS'
    except Exception as error:
        record.update(Status='FAIL', Error=str(error))
        raise
    finally:
        record['EndedUtc'] = utc()
        save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--verify-only', action='store_true')
    mode.add_argument('--output', type=Path)
    args = parser.parse_args()
    try:
        count = verify_originals(EVIDENCE)
        print(f'PASS {count} immutable originals and both SHA256 inventories', flush=True)
        if args.output:
            reproduce(args.output.resolve())
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
