import subprocess, pathlib, json, datetime, time

root = pathlib.Path('/workspace/scratch/02a55de33070')
work = root / 'audit-work'
command = ['/tmp/ipc-audit-dotnet/dotnet', 'tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll', '.', '--baseline']
result = {'source_sha':'8cabf06d3c6d53ba551b65e58a13db6cdcaba16a', 'command':command, 'cwd':str(root/'importer-pc-audit-build'), 'reason':'The first zero-exit run lacks the final stdout completion marker, although final-case artifacts exist. Repeat the unchanged selected scope with pipe capture to establish completion independently.', 'started_at':datetime.datetime.now(datetime.timezone.utc).isoformat()}
start = time.monotonic()
run = subprocess.run(command, cwd=root/'importer-pc-audit-build', stdout=subprocess.PIPE, stderr=subprocess.PIPE)
(work/'verification-baseline-recheck.stdout.log').write_bytes(run.stdout)
(work/'verification-baseline-recheck.stderr.log').write_bytes(run.stderr)
output = run.stdout.decode(errors='replace')
result.update(exit_code=run.returncode, seconds=round(time.monotonic()-start,3), stdout_bytes=len(run.stdout), stderr_bytes=len(run.stderr), final_pass_marker='PASS selected verification scope --baseline' in output, ended_at=datetime.datetime.now(datetime.timezone.utc).isoformat())
(work/'verification-baseline-recheck.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
print(json.dumps(result, ensure_ascii=False))
print('\n'.join(output.splitlines()[-16:]))
