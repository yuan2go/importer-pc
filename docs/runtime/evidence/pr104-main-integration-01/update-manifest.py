import copy, hashlib, json, subprocess
from pathlib import Path

def git(*args, **kwargs):
    return subprocess.check_output(['git',*args],**kwargs)
owned={'.gitattributes','docs/INDEX.md','docs/runtime/win11-24x7-stability-remediation-01.md',
       'tests/Ipc.Application.Verification/Program.cs',
       'docs/work-packages/WP-IPC-CODE-RELIABILITY-HARDENING-02.md',
       'docs/runtime/code-reliability-hardening-02.md',
       'docs/runtime/validation/pr104-main-integration-01.md','scripts/check-pr104-integration.py'}
evidence=Path('docs/runtime/evidence/pr104-main-integration-01')
if evidence.exists():
    owned.update(p.as_posix() for p in evidence.rglob('*') if p.is_file())
subprocess.run(['git','add','--',*sorted(owned)],check=True)
manifest=json.loads(Path('docs/manifest.json').read_text(encoding='utf-8-sig'))
records={r['path']:r for r in manifest['files']}
for path in sorted(owned):
    data=git('show',':'+path)
    if path in records:
        row=records[path]
        if 'pre_pr104_main_integration_01_records' not in row and 'pre_pr104_main_integration_01_identity' not in row:
            row['pre_pr104_main_integration_01_identity']={k:row[k] for k in ['git_blob','sha256','size_bytes','version','status','source'] if k in row}
    else:
        row={'path':path};manifest['files'].append(row)
    row.update(sha256=hashlib.sha256(data).hexdigest(),size_bytes=len(data),git_blob=git('hash-object','--stdin',input=data).decode().strip(),
               version='pr104-main-integration-01',status='scoped_integration_or_separately_identified_evidence',
               purpose='Preserve PR103 original failed qualification and PR104 reliability work while synchronizing main',
               source='User-authorized PR104 integration 2026-10-25; original source and evidence identities retained',storage_scope='public_repository')
Path('docs/manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
subprocess.run(['git','add','docs/manifest.json'],check=True)
sha=hashlib.sha256(git('show',':docs/manifest.json')).hexdigest()
Path('docs/manifest.sha256').write_text(sha+'  manifest.json\n',encoding='utf-8',newline='\n')
subprocess.run(['git','add','docs/manifest.sha256'],check=True)
print(json.dumps({'UpdatedOwnedRecords':len(owned),'ManifestRecords':len(manifest['files']),'CanonicalSha256':sha}))
