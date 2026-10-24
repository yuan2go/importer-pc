$ErrorActionPreference='Stop'
$evidence='D:/projects/ipc-retention-full-scale-20261024'
$run=Get-Content -LiteralPath (Join-Path $evidence 'PROTECTED-RESULT.json') -Raw | ConvertFrom-Json
$live=Get-Content -LiteralPath (Join-Path $evidence 'LIVE.json') -Raw | ConvertFrom-Json
if($run.Status -ne 'InProgress' -or $live.Name -ne 'create-2592000'){throw 'The expected synthetic generator is not active'}
$child=Get-CimInstance Win32_Process -Filter ('ProcessId='+$live.Pid)
if($child.Name -ne 'dotnet.exe' -or $child.ParentProcessId -ne 62520 -or -not $child.CommandLine.Contains($run.ProtectedRoot,[StringComparison]::OrdinalIgnoreCase) -or -not $child.CommandLine.Contains('--retention-qualification') -or -not $child.CommandLine.Contains('create')){throw 'Refuse to stop an unverified process'}
$record=[ordered]@{Pid=$child.ProcessId;Parent=$child.ParentProcessId;CommandLine=$child.CommandLine;Reason='Controlled termination of this explicitly synthetic generator: input-envelope fixture was simplified and cannot qualify complete historical structure';AtUtc=[DateTimeOffset]::UtcNow.ToString('o');PartialDatabase='Preserved; no deletion or lease removal'}
Stop-Process -Id $child.ProcessId -Force
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'INVALID-FIXTURE-STOP.json') -Encoding utf8NoBOM
