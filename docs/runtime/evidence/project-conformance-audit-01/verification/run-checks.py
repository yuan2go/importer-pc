import subprocess, pathlib, json, datetime, os, time

root = pathlib.Path('/workspace/scratch/02a55de33070')
work = root / 'audit-work'
source = root / 'importer-pc-audit-build'
dotnet = '/tmp/ipc-audit-dotnet/dotnet'
sha = '8cabf06d3c6d53ba551b65e58a13db6cdcaba16a'
architecture = (work / 'verification-architecture.log').read_text()
line = [x for x in architecture.splitlines() if x.startswith('Evidence data retained at ')][-1]
area = pathlib.Path(line.split(' at ', 1)[1])
plugin = area / 'architecture-13/IntegrationExtension/output/Ipc.Reference.IntegrationExtension.dll'
checks = [
 ('baseline', [dotnet, 'tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll', '.', '--baseline'], None),
 ('followup', [dotnet, 'tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll', '.', '--followup'], None),
 ('contradiction-build', [dotnet, 'build', str(work/'contradiction-probe/ContradictionProbe.csproj'), '-m:1', '-nr:false', '--verbosity', 'minimal'], None),
 ('contradiction-run', [dotnet, str(work/'contradiction-probe/bin/Debug/net10.0/ContradictionProbe.dll'), str(work/'contradiction-evidence')], 'contradiction-build'),
 ('health-build', [dotnet, 'build', str(work/'health-repro/HealthRepro.csproj'), '-m:1', '-nr:false', '--verbosity', 'minimal'], None),
 ('health-run', [dotnet, str(work/'health-repro/bin/Debug/net10.0/HealthRepro.dll'), str(work/'health-evidence'), str(plugin), sha], 'health-build'),
]
results=[]
for name,command,requires in checks:
 result={'name':name,'source_sha':sha,'command':command,'cwd':str(source),'started_at':datetime.datetime.now(datetime.timezone.utc).isoformat()}
 if requires and not any(x['name']==requires and x.get('exit_code')==0 for x in results):
  result['status']='NOT_RUN_DEPENDENCY_FAILED'
 else:
  logfile=work/('verification-'+name+'.log'); start=time.monotonic()
  print('RUN '+name,flush=True)
  with logfile.open('w') as output:
   process=subprocess.run(command,cwd=source,stdout=output,stderr=subprocess.STDOUT)
  result.update(exit_code=process.returncode,seconds=round(time.monotonic()-start,3),log=str(logfile))
  print(name+' exit='+str(process.returncode)+' seconds='+str(result['seconds']),flush=True)
 result['ended_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();results.append(result)
 (work/'verification-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2))
