import copy,hashlib,json,subprocess
from pathlib import Path
BASE='ed42da8187be08d9a65e067313d3039e2f2e9ebd'
def git(*args):return subprocess.check_output(['git',*args])
m=json.loads(git('show',BASE+':docs/manifest.json'));old={r['path']:r for r in m['files']}
owned={p.decode() for p in git('diff','--cached','--name-only','-z',BASE).split(b'\0') if p}-{'docs/manifest.json','docs/manifest.sha256'}
index={}
for line in git('ls-files','--stage','-z').split(b'\0'):
    if line:
        meta,path=line.split(b'\t',1);mode,sha,stage=meta.split();assert stage==b'0';index[path.decode()]=sha.decode()
shas=list(dict.fromkeys(index[p] for p in owned));raw=subprocess.run(['git','cat-file','--batch'],input=('\n'.join(shas)+'\n').encode(),stdout=subprocess.PIPE,check=True).stdout
blobs={};cursor=0
for sha in shas:
    end=raw.index(b'\n',cursor);actual,kind,size=raw[cursor:end].split();size=int(size);cursor=end+1;assert actual.decode()==sha and kind==b'blob';blobs[sha]=raw[cursor:cursor+size];cursor+=size+1
assert cursor==len(raw)
for path in sorted(owned):
    row=old.get(path)
    if row is None:row={'path':path};m['files'].append(row)
    else:row['pre_retention_ingress_followup_03_identity']={k:copy.deepcopy(row[k]) for k in ('git_blob','sha256','size_bytes','version','status','source') if k in row}
    b=blobs[index[path]];row.update(git_blob=index[path],sha256=hashlib.sha256(b).hexdigest(),size_bytes=len(b),version='retention-ingress-full-retest-03-followup',status='scoped_reuse_tool_fix_or_separately_identified_synthetic_evidence',source='User-approved full protected qualification; fixed productb83998f1 and all original evidence identities retained; follow-up to merged PR105',storage_scope='public_repository')
run=json.loads(Path('D:/projects/ipc-retention-ingress-full-retest-20261025/protected/PROTECTED-RESULT.json').read_text(encoding='utf-8-sig'))
assert run['Status']!='InProgress' and run.get('EndedUtc'), 'Terminal run required for final inventory'
assert run['SourceCommit']=='b83998f18c56df2dd965630b25908feedc00a56f'
m['retention_ingress_full_retest_03_followup']={'main':BASE,'product_source':run['SourceCommit'],'report':'docs/runtime/validation/retention-ingress-full-retest-03.md','evidence':'docs/runtime/evidence/retention-ingress-full-retest-03/FILES.json','terminal_receipt':'docs/runtime/evidence/retention-ingress-full-retest-03/original/completed-run/protected/PROTECTED-RESULT.json','product_config_changes':False,'phase':run['Status'],'case_statuses':{str(c['Count']):c['Status'] for c in run['Cases']},'repeated_reuse_fix':'New metadata copy updates immediate parent fields; original parent/ancestor files unchanged','qualification':'No target IPC, real SDK/RCS, 24h/72h or field acceptance','inventory_scope':'Owned staged delta against merged PR105 only; all unrelated historical entries/root metadata retained. Earlier full-retest root key is the preserved preterminal publication checkpoint.'}
out=(json.dumps(m,indent=2,ensure_ascii=False)+'\n').encode();Path('docs/manifest.json').write_bytes(out);Path('docs/manifest.sha256').write_text(hashlib.sha256(out).hexdigest()+'  manifest.json\n',encoding='utf-8')
print(json.dumps({'OwnedRows':len(owned),'ManifestSHA256':hashlib.sha256(out).hexdigest()}))
