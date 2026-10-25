import argparse,hashlib,json,subprocess
from pathlib import Path
from datetime import datetime,timezone
p=argparse.ArgumentParser();p.add_argument('--head',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
main='404e85523550203bf02dd90e666fa0305b1d5ed0';pr='10ad5d264b7e3f1b581dcf3fd044163fda02dcef';head=a.head
def git(*args):return subprocess.check_output(['git',*args])
def tree(ref):
    out={}
    for row in git('ls-tree','-r','-z',ref).split(b'\0'):
        if row:
            meta,path=row.split(b'\t',1);mode,kind,sha=meta.split();assert kind==b'blob';out[path.decode()]=sha.decode()
    return out
t={r:tree(r) for r in (main,pr,head)}
owned={x.decode() for x in git('diff','--name-only','-z',main,head).split(b'\0') if x}-{'docs/manifest.json','docs/manifest.sha256'}
requested={(main,'docs/manifest.json'),(head,'docs/manifest.json'),(head,'docs/manifest.sha256')}|{(head,x) for x in owned}|{(main,'src/Ipc.Application.ParcelFlow/ParcelRepository.cs'),(head,'src/Ipc.Application.ParcelFlow/ParcelRepository.cs')}
shas=list(dict.fromkeys(t[r][path] for r,path in requested));data=subprocess.run(['git','cat-file','--batch'],input=('\n'.join(shas)+'\n').encode(),stdout=subprocess.PIPE,check=True).stdout
blobs={};pos=0
for sha in shas:
    end=data.index(b'\n',pos);actual,kind,size=data[pos:end].split();size=int(size);pos=end+1;assert actual.decode()==sha and kind==b'blob';blobs[sha]=data[pos:pos+size];pos+=size+1
assert pos==len(data)
def blob(ref,path):return blobs[t[ref][path]]
old=json.loads(blob(main,'docs/manifest.json'));raw=blob(head,'docs/manifest.json');m=json.loads(raw);rows={r['path']:r for r in m['files']}
assert len(rows)==len(m['files'])
for path in owned:
    b=blob(head,path);row=rows[path];assert row['git_blob']==t[head][path] and row['sha256']==hashlib.sha256(b).hexdigest() and row['size_bytes']==len(b),path
assert [r for r in m['files'] if r['path'] not in owned]==[r for r in old['files'] if r['path'] not in owned]
for key,value in old.items():
    if key!='files':assert m[key]==value,key
assert blob(head,'docs/manifest.sha256').decode().split()==[hashlib.sha256(raw).hexdigest(),'manifest.json']
kept_main=[path for path in t[main] if path.startswith('docs/runtime/evidence/')]
for path in kept_main:assert t[head].get(path)==t[main][path],path
kept_pr=[path for path in t[pr] if path.startswith('docs/runtime/evidence/retention-ingress-02/')]
for path in kept_pr:assert t[head].get(path)==t[pr][path],path
for path in ('src/Ipc.Application.ParcelFlow/ParcelRuntime.cs','src/Ipc.Application.ParcelFlow/ParcelCommands.cs','src/Ipc.Platform/ObservationSession.cs','src/Ipc.Platform/RuntimeJournal.cs'):
    assert t[main][path]==t[head][path],path
def migrations(b):return b[b.index(b'static readonly DatabaseMigration[] migrations'):b.index(b'public long Revision')]
assert migrations(blob(main,'src/Ipc.Application.ParcelFlow/ParcelRepository.cs'))==migrations(blob(head,'src/Ipc.Application.ParcelFlow/ParcelRepository.cs'))
assert not a.output.exists()
result={'Status':'PASS_COMMITTED_INTEGRATION_AND_ORIGINAL_IDENTITY','AtUtc':datetime.now(timezone.utc).isoformat(),'Head':head,'Main':main,'OriginalPr105':pr,'OwnedManifestRows':len(owned),'MainEvidenceFilesUnchanged':len(kept_main),'OriginalPr105EvidenceFilesUnchanged':len(kept_pr),'MainUnrelatedManifestRowsAndRootMetadataUnchanged':True,'Sql1To9Unchanged':True,'MainRuntimeCommandEpochJournalChangesRetained':True,'Scope':'Committed bytes only. Does not grant pending full protected maintenance, target IPC, real SDK/RCS, 24h/72h or field acceptance.'}
a.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps(result))
