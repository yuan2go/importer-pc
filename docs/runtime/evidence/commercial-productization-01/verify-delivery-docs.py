from pathlib import Path
import subprocess,json,hashlib,re,urllib.parse
base='c445b0ec32a3db44f00a1968f3c501aa36eb786f'
_paths=subprocess.check_output(['git','diff','--cached','--name-only',base]).decode().splitlines()
_index={}
for row in subprocess.check_output(['git','ls-files','-s','-z']).split(b'\0'):
 if row:
  header,path=row.split(b'\t',1);_index[path.decode()]=header.decode().split()[1]
_wanted=sorted(set(_index[p] for p in _paths))
_result=subprocess.run(['git','cat-file','--batch'],input=('\n'.join(_wanted)+'\n').encode(),stdout=subprocess.PIPE,check=True).stdout
_cache={};_offset=0
for blob in _wanted:
 end=_result.index(b'\n',_offset);header=_result[_offset:end].split();size=int(header[2]);_offset=end+1;_cache[blob]=_result[_offset:_offset+size];_offset+=size+1
def git(*args):
 if len(args)==2 and args[1].startswith(':') and args[1][1:] in _index:
  identity=_index[args[1][1:]]
  if args[0]=='rev-parse':return (identity+'\n').encode()
  if args[0]=='show' and identity in _cache:return _cache[identity]
 return subprocess.check_output(['git',*args])
manifest=json.loads(git('show',':docs/manifest.json'));old=json.loads(git('show',base+':docs/manifest.json'))
paths=git('diff','--cached','--name-only',base).decode().splitlines();assert not any(p.endswith('.xlsx') or p.startswith('.artifacts/') for p in paths)
assert not git('diff','--cached',base,'--','docs/ui','docs/reviews/issues.json','docs/reviews/STATUS.md','docs/readiness/decision-pack.md','docs/readiness/technical-baseline.json').strip()
records={r['path']:r for r in manifest['files']};previous={r['path']:r for r in old['files']}
changed=set(paths)-{'docs/manifest.json','docs/manifest.sha256'}
for name in changed:
 data=git('show',':'+name);r=records[name]
 assert r['sha256']==hashlib.sha256(data).hexdigest() and r['size_bytes']==len(data) and r['git_blob']==git('rev-parse',':'+name).decode().strip(),name
for name,r in previous.items():
 if name not in changed:assert records[name]==r,name
for key,value in old.items():
 if key not in {'files','branch','framework_design_state','last_updated'}:assert manifest[key]==value,key
assert hashlib.sha256(git('show',':docs/manifest.json')).hexdigest()==git('show',':docs/manifest.sha256').decode().split()[0]
archive=Path('docs/archive/pre-commercial-productization-01-implementation');inventory=json.loads((archive/'inventory.json').read_bytes())
for f in inventory['files']:
 raw=git('show',inventory['source']+':'+f['path']);snapshot=git('show',':'+(archive/f['snapshot']).as_posix())
 assert raw==snapshot and hashlib.sha256(raw).hexdigest()==f['sha256'] and git('rev-parse',inventory['source']+':'+f['path']).decode().strip()==f['git_blob']
# Validate additions in current Markdown. Historical original text is verified by its unchanged Git blob.
added={};current=None
for line in git('diff','--cached','--unified=0',base,'--','*.md').decode().splitlines():
 if line.startswith('+++ b/'):current=line[6:];added.setdefault(current,[])
 elif line.startswith('+') and not line.startswith('+++') and current:added[current].append(line[1:])
for name,lines in added.items():
 for line in lines:
  for match in re.finditer(r'!?\[[^\]]*\]\((<[^>]*>|[^)\s]+)(?:\s+"[^"]*")?\)',line):
   value=match.group(1).strip('<>');url=urllib.parse.urlsplit(value)
   if url.scheme or value.startswith('//'):continue
   target=(Path(name).parent/urllib.parse.unquote(url.path)).resolve() if url.path else Path(name).resolve()
   assert target.exists(),(name,value)
   if url.fragment and target.suffix=='.md':
    content=target.read_text(encoding='utf-8');headers=re.findall(r'^#{1,6}\s+(.+)$',content,re.M)
    anchors={re.sub(r'[^\w\-\s]','',h.lower()).replace(' ','-') for h in headers}
    assert urllib.parse.unquote(url.fragment) in anchors or f'id="{url.fragment}"' in content,(name,value,'anchor')
for name in changed:
 data=git('show',':'+name)
 assert not re.search(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',data),name
 assert not re.search(rb'(?<![A-Za-z0-9])(?:ghp_|github_pat_)[A-Za-z0-9_]{24,}',data),name
# Final actual receipts agree on one unchanged medium.
e=Path('docs/runtime/evidence/commercial-productization-01');receipts=[json.loads((e/n).read_bytes()) for n in ['core-result.json','wpf-result.json','component-result.json']]
assert len({r['SourceCommit'] for r in receipts})==1 and len({r['MediumInventorySha256'].lower() for r in receipts})==1 and all(r['Status']=='PASS' for r in receipts)
assert receipts[0]['UnchangedMedium'] and receipts[2]['UnchangedInputs']
print('PASS canonical manifest hashes, sizes and Git blobs for every changed/new authorized file')
print('PASS untouched file records and all historical publication objects preserved; self hash valid')
print('PASS seven original Git-byte snapshots; D/RV/technical baseline and original UI assets unchanged')
print('PASS added current-document local links and anchors; historical custody is checked separately')
print('PASS public delta excludes private key/token material, local artifacts and unrelated user workbook')
print('PASS core/WPF/component receipts agree on one frozen source and medium inventory')
