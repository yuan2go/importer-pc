$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$file='D:/projects/ipc-retention-ingress-full-retest-20261025/EXECUTED-HELPER.ps1'
$source=Get-Content -LiteralPath $file -Raw
$match=[regex]::Match($source,'(?s)  \$sourceBytes=\$null;.*?(?=  if\(\$priorRun\)\{\s+\$priorCases=)')
if(-not $match.Success){throw 'Expected actual published capacity block'}
$policy=[ScriptBlock]::Create($match.Value)
$priorRun=Get-Content -LiteralPath D:/projects/ipc-retention-full-scale-corrected-20261024/PROTECTED-RESULT.json -Raw|ConvertFrom-Json
$priorRoot=$priorRun.ProtectedRoot;$RestoreAll=$false
$measuredFree=(Microsoft.PowerShell.Management\Get-PSDrive D).Free
$script:capacityAvailable=$measuredFree
function Get-PSDrive {param($Name) [pscustomobject]@{Free=$script:capacityAvailable}}
function Save-Result {}
$checks=@()
foreach($count in @(1000)){
 $result=[ordered]@{Cases=@()};. $policy
 if($result.CapacityChecks[0].CopyFactor -ne 4.15 -or $result.CapacityChecks[0].ReserveBytes -ne 8GB){throw 'First case omitted a required copy or lowered reserve'}
 $checks+='First case accounts source, complete backup, restore and repeated-maintenance backup plus unchanged reserve'
}
foreach($count in @(604800)){
 $result=[ordered]@{Cases=@([pscustomobject]@{Count=1000;OriginalBytes=16007168L})};. $policy
 if($result.CapacityChecks[0].SourceBytes -ne 9464315904L -or $result.CapacityChecks[0].CopyFactor -ne 2.15 -or $result.Cases.Count -ne 1){throw 'Full case did not use actual completed-backup bytes'}
 $checks+='Full case uses exact existing backup bytes and unchanged copy factor/reserve, not small-fixture extrapolation'
}
$script:capacityAvailable=0
foreach($count in @(604800)){
 $result=[ordered]@{Cases=@([pscustomobject]@{Count=1000;OriginalBytes=16007168L})};. $policy
 throw 'Insufficient-space capacity block failed to stop this case'
}
if($result.Cases[-1].Status -ne 'NOT_RUN_DISK_CAPACITY'){throw 'Missing explicit capacity refusal'}
$checks+='Controlled insufficient-space input refuses before any restore; no actual disk is filled'
[ordered]@{Status='PASS_CAPACITY_POLICY_COMPONENT_ONLY';AtUtc=[DateTimeOffset]::UtcNow.ToString('o');HelperSha256=(Get-FileHash -LiteralPath $file).Hash;MeasuredFreeBytes=$measuredFree;NegativeAvailableBytes=0;NegativeFreeSpaceIsStubbed=$true;ElevatedOrProtectedCliExecuted=$false;Checks=$checks}|ConvertTo-Json -Depth 5|Set-Content D:/projects/ipc-retention-ingress-full-retest-20261025/CAPACITY-POLICY-CHECK.json -Encoding utf8NoBOM
Get-Content D:/projects/ipc-retention-ingress-full-retest-20261025/CAPACITY-POLICY-CHECK.json
