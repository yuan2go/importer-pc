import copy, json, re, subprocess
from pathlib import Path

OLD = '168c713d4fe2c332c9ee2964e7cfaa69de3e4b97'
MAIN = 'c425435c6f980ca537a29e4e8353d12be341df61'
BASE = '1dfb676a2970f80e80d635303d84dcec75676cf5'
def blob(ref, path):
    return subprocess.check_output(['git','show',f'{ref}:{path}'])
def save(path, data):
    Path(path).write_text(data, encoding='utf-8', newline='\n')

for path in ['.gitattributes', 'tests/Ipc.Application.Verification/Program.cs']:
    text = Path(path).read_text(encoding='utf-8')
    text, count = re.subn(r'^<<<<<<< HEAD\n(.*?)^=======\n(.*?)^>>>>>>> origin/main\n', lambda m:m[1]+m[2], text, flags=re.M|re.S)
    assert count == 1, (path, count)
    save(path, text)
path='docs/runtime/win11-24x7-stability-remediation-01.md'
ours=blob(OLD,path).decode('utf-8')
theirs=blob(MAIN,path).decode('utf-8')
assert theirs.splitlines()[2].startswith('2026-10-24')
save(path, ours.replace('\n\n','\n\n'+theirs.splitlines()[2]+'\n\n',1))

base, ours, theirs = [json.loads(blob(ref,'docs/manifest.json')) for ref in [BASE,OLD,MAIN]]
merged=copy.deepcopy(ours)
for key in theirs.keys() | base.keys():
    if key=='files': continue
    if theirs.get(key)==base.get(key): continue
    assert ours.get(key)==base.get(key) or ours.get(key)==theirs.get(key), ('top-level conflict',key)
    merged[key]=copy.deepcopy(theirs[key])
maps=[{r['path']:r for r in m['files']} for m in [base,ours,theirs]]
assert all(len(mp)==len(m['files']) for mp,m in zip(maps,[base,ours,theirs]))
bm,om,tm=maps
records=[]; combined=[]
for path in dict.fromkeys([r['path'] for r in ours['files']]+[r['path'] for r in theirs['files']]):
    b,o,t=bm.get(path),om.get(path),tm.get(path)
    if t==b or t==o: result=copy.deepcopy(o)
    elif o==b: result=copy.deepcopy(t)
    else:
        assert path in ['.gitattributes','docs/INDEX.md','docs/runtime/win11-24x7-stability-remediation-01.md','tests/Ipc.Application.Verification/Program.cs'], path
        result=copy.deepcopy(t)
        result.update(copy.deepcopy(o))
        result['pre_pr104_main_integration_01_records']={'pr104':o,'main_pr103':t}
        combined.append(path)
    assert result is not None, path
    records.append(result)
merged['files']=records
merged['pr104_main_integration_01']={
 'date':'2026-10-25','source_pr_head':OLD,'baseline_main':MAIN,'merge_base':BASE,
 'method':'Merge main into PR104 without rebase or force push; retain both parent identities',
 'product_source':'Unchanged from PR104; PR103 only adds qualification tools and evidence',
 'retention_defect':'604800 full maintenance FAIL, 2592000 NOT_RUN and 604800 restore cancellation retained; PR105 remains separate',
 'qualification':'Final head CI/review/release receipts required; target IPC, actual SDK/RCS, 24h/72h and field acceptance remain unverified',
 'report':'docs/runtime/validation/pr104-main-integration-01.md'}
save('docs/manifest.json',json.dumps(merged,ensure_ascii=False,indent=2)+'\n')
save('docs/manifest.sha256','PENDING_CANONICAL_INDEX_HASH  manifest.json\n')
print(json.dumps({'files':len(records),'combined_manifest_records':combined,'main_qualification_section_unchanged':merged['retention_maintenance_qualification_01']==theirs['retention_maintenance_qualification_01']},ensure_ascii=False))
