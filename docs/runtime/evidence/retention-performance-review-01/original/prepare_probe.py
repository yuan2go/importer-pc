"""Extract exact baseline/current SQL; use the current build's native SQLite provider."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

repo, out = map(Path, sys.argv[1:3])
baseline = '5074660fd10e7a91d81923edac782891e377a519'
out.mkdir(parents=True, exist_ok=True)
def git(*args):
    return subprocess.check_output(['git', '-C', str(repo), *args]).decode('utf-8-sig')
current = (repo/'src/Ipc.Application.ParcelFlow/ParcelHistoryRetention.cs').read_text(encoding='utf-8-sig')
old = git('show', baseline+':src/Ipc.Application.ParcelFlow/ParcelHistoryRetention.cs')
for name, source in [('old', old), ('new', current)]:
    sql = re.search(r'bool RetentionClear[\s\S]+?c.CommandText = """([\s\S]+?)""";', source)[1]
    (out/(name+'.sql')).write_text(sql, encoding='utf-8')
prepare = re.search(r'void PrepareRetentionCommandOwners[\s\S]+?c.CommandText = """([\s\S]+?)""";', current)[1]
(out/'prepare.sql').write_text(prepare, encoding='utf-8')
schema_path = 'src/Ipc.Application.ParcelFlow/ParcelRepository.cs'
schema = (repo/schema_path).read_text(encoding='utf-8-sig')
migrations = re.findall(r'new\((\d+), """(.*?)"""\)', schema, re.S)
prior_migrations = re.findall(r'new\((\d+), """(.*?)"""\)', git('show', baseline+':'+schema_path), re.S)
assert migrations == prior_migrations and [n for n, _ in migrations] == list(map(str, range(1, 10)))
(out/'schema.sql').write_text('\n'.join(sql for _, sql in migrations), encoding='utf-8')
binary = repo/'tests/Ipc.Application.Verification/bin/Release/net10.0'
dlls = [binary/'Microsoft.Data.Sqlite.dll', *binary.glob('SQLitePCLRaw*.dll')]
native = binary/'runtimes/win-x64/native/e_sqlite3.dll'
assert native.exists() and all(p.exists() for p in dlls)
refs = ''.join(f'<Reference Include="{p.stem}"><HintPath>{p}</HintPath></Reference>' for p in dlls)
(out/'Probe.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/>{refs}<Content Include="{native}" Link="e_sqlite3.dll" CopyToOutputDirectory="Always"/></ItemGroup></Project>', encoding='utf-8')
(out/'IDENTITY.json').write_text(json.dumps({
    'SourceCommit': git('rev-parse', 'HEAD').strip(), 'SourceTree': git('rev-parse', 'HEAD^{tree}').strip(),
    'Baseline': baseline, 'MigrationSql1To9Unchanged': True,
    'MigrationHashes': {n: hashlib.sha256(sql.encode()).hexdigest() for n, sql in migrations},
    'BinaryHashes': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [*dlls, native]},
}, indent=2), encoding='utf-8')
print(out)
