from pathlib import Path
import subprocess,json,datetime
base=Path('D:/audits/importer-pc/AQA01-remediation-20261023');package=base/'unpacked/sportmaster-win-x64';out=base/'native-entries';out.mkdir(exist_ok=False)
c=json.loads((package/'app/config/business.json').read_bytes());assert c['Mode']=='Simulation';assert all(r['Driver']=='manual' for r in c['Topology']['Readers'])
records=[]
for name,args,stdin in [
 ('native-console',[str(package/'app/Ipc.Assembly.exe'),'--config',str(package/'app/config'),'--data-root',str(out/'runtime-data')],'snapshot\nquit\n'),
 ('desktop-environment',[str(package/'desktop/Ipc.Desktop.exe'),'--environment-report'],None)]:
 p=subprocess.run(args,input=stdin,text=True,capture_output=True,timeout=30)
 (out/(name+'.stdout.log')).write_text(p.stdout,encoding='utf-8');(out/(name+'.stderr.log')).write_text(p.stderr,encoding='utf-8')
 assert p.returncode==0,(name,p.returncode,p.stderr)
 if name=='native-console':assert '"Started"' in p.stdout and '"Simulation"' in p.stdout
 else:assert len(p.stdout)>0
 records.append(dict(Name=name,Command=args,Stdin=stdin,ExitCode=p.returncode,WindowsNativeUi='NOT_RUN: environment report exits before window creation' if name=='desktop-environment' else 'Not a UI test'))
result=dict(At=datetime.datetime.now(datetime.timezone.utc).isoformat(),SourceCommit=json.loads((package/'ASSEMBLY.json').read_bytes())['SourceCommit'],Checks=records)
(out/'RESULT.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n');print(json.dumps(result,ensure_ascii=False))
