import argparse,hashlib,json,subprocess
from pathlib import Path
from datetime import datetime,timezone
parser=argparse.ArgumentParser();parser.add_argument('--head',required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
base='1dfb676a2970f80e80d635303d84dcec75676cf5'
def git(*a):return subprocess.check_output(['git',*a])
def blob(ref,path):return git('show',ref+':'+path)
assert not args.output.exists()
head=args.head
old=json.loads(blob(base,'docs/manifest.json'));raw=blob(head,'docs/manifest.json');new=json.loads(raw)
owned={p.decode() for p in git('diff','--name-only','-z',base,head).split(b'\0') if p}-{'docs/manifest.json','docs/manifest.sha256'}
rows={r['path']:r for r in new['files']};assert len(rows)==len(new['files'])
for path in owned:
 data=blob(head,path);row=rows[path]
 assert row['sha256']==hashlib.sha256(data).hexdigest() and row['size_bytes']==len(data),path
 assert row['git_blob']==git('rev-parse',head+':'+path).decode().strip(),path
 assert row['storage_scope']=='public_repository',path
assert [r for r in new['files'] if r['path'] not in owned]==[r for r in old['files'] if r['path'] not in owned]
for key,value in old.items():
 if key!='files':assert new[key]==value,key
checksum=blob(head,'docs/manifest.sha256').decode().split();assert checksum==[hashlib.sha256(raw).hexdigest(),'manifest.json']
evidence='docs/runtime/evidence/retention-maintenance-qualification-01/'
files=json.loads(blob(head,evidence+'FILES.json'))
expected={evidence+r['path'] for r in files}
actual={p.decode() for p in git('ls-tree','-r','--name-only','-z',head,'--',evidence+'original').split(b'\0') if p}
assert expected==actual and len(files)==len(expected)
for row in files:
 data=blob(head,evidence+row['path']);assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
assert not git('diff',base,head,'--','src','config')
value={'Status':'PASS_OWNED_DELTA_AND_ARCHIVE_IDENTITY','AtUtc':datetime.now(timezone.utc).isoformat(),'Head':head,'Base':base,'OwnedManifestRows':len(owned),'OriginalTextFiles':len(files),'UnrelatedHistoricalManifestRowsAndMetadataUnchanged':True,'ProductAndConfigChanges':False,'Scope':'Canonical committed Git bytes only; not current uncommitted edits, whole historical archive verification, full-scale CLI completion or field acceptance'}
args.output.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8');print(json.dumps(value))
