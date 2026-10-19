from pathlib import Path
import json, subprocess,hashlib
text=Path('docs/runtime/evidence/commercial-productization-01/verify-delivery-docs.py').read_text()
text=text.replace("base='c445b0ec32a3db44f00a1968f3c501aa36eb786f'", "base='aaa30b57cf8f810637b6502748783cdab66bbf36'")
exec(compile(text,'inherited-delivery-verifier-latest-main-base','exec'))
folder=Path('docs/runtime/evidence/pr47-main-integration')
receipts=[json.loads((folder/n).read_bytes()) for n in ['core-result.json','wpf-result.json','component-result.json','removal-result.json']]
assert all(r['Status']=='PASS' for r in receipts)
assert {r['SourceCommit'] for r in receipts}=={'55025ce9c62173fea5cdc5ceecfb29453ed5e7fa'}
assert len({r['MediumInventorySha256'].lower() for r in receipts})==1
old=json.loads(subprocess.check_output(['git','show','8dff0a1:docs/manifest.json']))
new=json.loads(subprocess.check_output(['git','show',':docs/manifest.json']))
assert old['pr47_commercial_productization_review']==new['pr47_commercial_productization_review']
assert all(m['SourceCommit']=='55025ce9c62173fea5cdc5ceecfb29453ed5e7fa' for m in new['pr47_latest_main_integration']['media'])
print('PASS all latest-main publication metadata plus original PR47 review object preserved; final55025ce media/receipts agree')
