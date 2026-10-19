from pathlib import Path
import json,hashlib,subprocess,struct
repo=Path('D:/w/ipc-release');root=Path('D:/r/latest-55025-final')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
r=json.loads((root/'RELEASE.json').read_bytes());assert r['State']=='PASS' and len(r['Packages'])==8 and len(r['ReusedArtifacts'])==8
original=json.loads((repo/'docs/runtime/evidence/pr47-main-integration/media-identities.json').read_bytes())
checks=[]
for o in original:
 n=next(p for p in r['Packages'] if p['Application']==o['Application'] and p['Target']==o['Target'])
 assert n['File']==o['Zip'] and n['Sha256']==o['ZipSha256'].upper() and n['Bytes']==o['ZipBytes']
 checks.append(dict(Check='ORIGINAL_IDENTITY_UNCHANGED',File=n['File'],Sha256=n['Sha256']))
media=root/'sportmaster-55025ce-win-x64';evidence=repo/'docs/runtime/evidence/pr47-main-integration'
for name in ('core-result.json','wpf-result.json','removal-result.json','component-result.json'):
 p=evidence/name;result=json.loads(p.read_bytes());assert result['SourceCommit']==r['SourceCommit'] and result['Status']=='PASS' and result['MediumInventorySha256']==sha(media/'SHA256SUMS')
 for item in result.get('Binaries',[]):assert sha(next(base/item['File'] for base in (media/'app',media/'desktop') if (base/item['File']).exists()))==item['Sha256']
 checks.append(dict(Check='INHERITED_SAME_BYTE_MEDIA_EVIDENCE',Path=p.relative_to(repo).as_posix(),EvidenceSha256=sha(p),SourceCommit=result['SourceCommit'],MediumInventorySha256=result['MediumInventorySha256'],Scope=result.get('Scope','See original evidence and log; no new execution is implied')))
for p in r['Packages']:
 if p['Target']=='win-x64':
  for path in ('app/Ipc.Assembly.exe','desktop/Ipc.Desktop.exe'):
   exe=root/Path(p['File']).stem/path;b=exe.read_bytes();assert b[:2]==b'MZ';offset=struct.unpack_from('<I',b,0x3c)[0];assert b[offset:offset+4]==b'PE\x00\x00' and struct.unpack_from('<H',b,offset+4)[0]==0x8664
   checks.append(dict(Check='PE_X64_ENTRY',File=p['File']+'/'+path,Sha256=sha(exe)))
entries=json.loads(Path('D:/w/latest-55025-entry-results.json').read_bytes());assert entries['SourceCommit']==r['SourceCommit'] and len(entries['Results'])==8
for p in entries['Results']:assert p['MediaSha256']==next(x['Sha256'] for x in r['Packages'] if x['Application']==p['Application'] and x['Target']==p['Target'])
checks.append(dict(Check='WINDOWS_READONLY_OR_LEGACY_ENTRY_GATE',ReceiptSha256=sha(Path('D:/w/latest-55025-entry-results.json')),Count=8,Scope=entries['Scope']))
result=dict(Status='PASS_TARGETED_RELEASE_CHECKS',SourceCommit=r['SourceCommit'],MainBaseline=r['MainBaseline'],BuildControllerCommit=r['BuildControllerCommit'],NonRuntimeMainInputDifferences=r['NonRuntimeMainInputDifferences'],Checks=checks,InheritedEvidence='Explicitly bound by unchanged actual inventory and binary hashes; not represented as rerun',WindowsFullAcceptance='NOT_RUN',CommercialQualification=False,RealSDKEquipmentField='BLOCKED',WholeEightArchiveRepeatBuild='NOT_RUN')
Path('D:/w/latest-55025-validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('PASS original6 unchanged, inherited frozen evidence4, PE entries6, fresh negative entries8')
