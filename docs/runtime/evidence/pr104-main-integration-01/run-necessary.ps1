. ./scripts/project-environment.ps1
$ErrorActionPreference='Stop'
$taskEvidence='D:\projects\ipc-pr104-integration-20261025'
$taskDll=Join-Path (Get-Location) 'tests\Ipc.Application.Verification\bin\Release\net10.0\Ipc.Application.Verification.dll'
$taskResults=[System.Collections.Generic.List[object]]::new()
function Invoke-TaskCase([string]$name,[string[]]$arguments) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $log=Join-Path $taskEvidence ($name+'.log')
    & dotnet $taskDll @arguments *> $log
    $code=$LASTEXITCODE
    $watch.Stop()
    $taskResults.Add([pscustomobject]@{Name=$name;Arguments=$arguments;ExitCode=$code;Seconds=$watch.Elapsed.TotalSeconds;Log=$log})
    $taskResults | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'LOCAL-CASES.json') -Encoding utf8
    Write-Output "$name exit=$code seconds=$($watch.Elapsed.TotalSeconds)"
    if($code -ne 0) { Get-Content -LiteralPath $log -Tail 14; throw "$name failed; original log retained" }
}
$taskIdentity=[ordered]@{Scope='Pending merge worktree snapshot, not final committed binary';PreviousPrHead=(git rev-parse HEAD);MergingMain=(git rev-parse MERGE_HEAD);ProductSourceTree=(git rev-parse 'HEAD:src');ConfigurationTree=(git rev-parse 'HEAD:config');ProductMatchesOriginalPrHead=$true;BuildLog='build-integration.log';Windows=$true;FixtureProgramWorktreeSha256=(Get-FileHash tests/Ipc.Application.Verification/Program.cs -Algorithm SHA256).Hash;Assemblies=@();Qualification='Synthetic only; no protected maintenance, large-scale rerun, installation or field qualification'}
foreach($path in @($taskDll,'src/Ipc.Application.ParcelFlow/bin/Release/net10.0/Ipc.Application.ParcelFlow.dll','src/Ipc.Platform/bin/Release/net10.0/Ipc.Platform.dll','src/Ipc.Application.Sportmaster/bin/Release/net10.0/Ipc.Application.Sportmaster.dll')) {
    $item=Get-Item -LiteralPath $path
    $taskIdentity.Assemblies += @{Path=$item.FullName;Bytes=$item.Length;Sha256=(Get-FileHash $item.FullName -Algorithm SHA256).Hash}
}
$taskIdentity | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'LOCAL-IDENTITY.json') -Encoding utf8
Copy-Item -LiteralPath tests/Ipc.Application.Verification/Program.cs -Destination (Join-Path $taskEvidence 'EXECUTED-Program.cs.txt')
Invoke-TaskCase 'reliability-02' @('.','--reliability-02')
Invoke-TaskCase 'h02-crashes' @('.','--h02-crashes')
$taskSeed=Join-Path $taskEvidence 'retention-seed'
$taskCase=Join-Path $taskEvidence 'retention-1000'
Invoke-TaskCase 'retention-seed' @('.','--retention-qualification','seed',$taskSeed)
Invoke-TaskCase 'retention-create-1000' @('.','--retention-qualification','create',$taskCase,$taskSeed,'1000')
Invoke-TaskCase 'retention-simulated-1000' @('.','--retention-qualification','exercise-simulated-authority',$taskCase)
Invoke-TaskCase 'retention-verify-1000' @('.','--retention-qualification','verify',$taskCase)
