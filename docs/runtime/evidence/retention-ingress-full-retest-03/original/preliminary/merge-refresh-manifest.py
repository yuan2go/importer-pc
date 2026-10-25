import copy,hashlib,json,subprocess
from pathlib import Path
MAIN='404e85523550203bf02dd90e666fa0305b1d5ed0';PR='10ad5d264b7e3f1b581dcf3fd044163fda02dcef';BASE='c425435c6f980ca537a29e4e8353d12be341df61'
def git(*a):return subprocess.check_output(['git',*a])
def manifest(ref):return json.loads(git('show',ref+':docs/manifest.json'))
main,pr,base=map(manifest,(MAIN,PR,BASE));value=copy.deepcopy(main)
rows={r['path']:r for r in value['files']};base_rows={r['path']:r for r in base['files']};pr_rows={r['path']:r for r in pr['files']}
def identity(row):return {k:copy.deepcopy(row[k]) for k in ('git_blob','sha256','size_bytes','version','status','source') if k in row}
for row in pr['files']:
    path=row['path']
    if row==base_rows.get(path):continue
    if path not in rows:
        added=copy.deepcopy(row);value['files'].append(added);rows[path]=added
    elif rows[path]==base_rows.get(path):
        target=rows[path];target.clear();target.update(copy.deepcopy(row))
    elif rows[path]!=row:
        target=rows[path]
        for key,item in row.items():
            if key not in target:target[key]=copy.deepcopy(item)
        target['pre_retention_ingress_main_integration_03_identities']={'main':identity(next(r for r in main['files'] if r['path']==path)),'pr105':identity(row)}
for key,item in pr.items():
    if key=='files':continue
    if key not in value:value[key]=copy.deepcopy(item)
    elif value[key]!=item and item!=base.get(key):
        assert value[key]==base.get(key),key
        value[key]=copy.deepcopy(item)
index={}
for line in git('ls-files','--stage','-z').split(b'\0'):
    if not line:continue
    meta,path=line.split(b'\t',1);mode,sha,stage=meta.split()
    if stage==b'0':index[path.decode()]=sha.decode()
owned={p.decode() for p in git('diff','--cached','--name-only','-z',MAIN).split(b'\0') if p}-{'docs/manifest.json','docs/manifest.sha256'}
shas=list(dict.fromkeys(index[p] for p in owned))
raw=subprocess.run(['git','cat-file','--batch'],input=('\n'.join(shas)+'\n').encode(),stdout=subprocess.PIPE,check=True).stdout
blobs={};cursor=0
for sha in shas:
    end=raw.index(b'\n',cursor);actual,kind,size=raw[cursor:end].split();size=int(size);cursor=end+1
    assert actual.decode()==sha and kind==b'blob';blobs[sha]=raw[cursor:cursor+size];cursor+=size+1
assert cursor==len(raw)
main_rows={r['path']:r for r in main['files']}
for path in sorted(owned):
    row=rows.get(path)
    if row is None:row={'path':path};rows[path]=row;value['files'].append(row)
    if path in main_rows:
        row.setdefault('pre_retention_ingress_main_integration_03_identities',{})['main']=identity(main_rows[path])
    if path in pr_rows:
        row.setdefault('pre_retention_ingress_main_integration_03_identities',{})['pr105']=identity(pr_rows[path])
    data=blobs[index[path]]
    row.update(sha256=hashlib.sha256(data).hexdigest(),size_bytes=len(data),git_blob=index[path],version='retention-ingress-full-retest-03',status='scoped_implementation_integration_or_separately_identified_synthetic_evidence',storage_scope='public_repository',source='User-approved PR105/main404e855 integration and protected complete-custody retest; original product5bb032d and a192b7f evidence identities retained')
value['retention_ingress_full_retest_03']={'main':MAIN,'original_pr105_head':PR,'report':'docs/runtime/validation/retention-ingress-full-retest-03.md','phase':'INTEGRATED_CODE_PENDING_FIXED_SOURCE_BUILD_AND_FULL_SCALE_CLI','original_evidence_identity':'PR103 FAIL/NOT_RUN and PR105 component/simulated results unchanged','qualification':'No target IPC, real SDK/RCS, 24h/72h or field acceptance','inventory_scope':'Canonical staged bytes for owned delta only; union of main and PR105 identities; no unrelated historical refresh'}
encoded=(json.dumps(value,ensure_ascii=False,indent=2)+'\n').encode();Path('docs/manifest.json').write_bytes(encoded);Path('docs/manifest.sha256').write_text(hashlib.sha256(encoded).hexdigest()+'  manifest.json\n',encoding='utf-8')
print(json.dumps({'OwnedRows':len(owned),'ManifestSha256':hashlib.sha256(encoded).hexdigest(),'MainHistoricalRootMetadataRetained':True}))
