param([string]$Evidence='D:/projects/ipc-retention-full-scale-corrected-20261024')
$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$observations=Join-Path $Evidence 'MAINTENANCE-OBSERVATIONS.jsonl'
if(Test-Path -LiteralPath $observations){throw 'Use a new observation file'}
while($true){
 try {
  $run=Get-Content (Join-Path $Evidence 'PROTECTED-RESULT.json') -Raw|ConvertFrom-Json
  $live=Get-Content (Join-Path $Evidence 'LIVE.json') -Raw|ConvertFrom-Json
  $case=$run.Cases|Where-Object Count -eq 604800
  $observedProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$($live.Pid)"|Select-Object -First 1
  $point=[ordered]@{AtUtc=[DateTimeOffset]::UtcNow.ToString('o');WholeStatus=$run.Status;Stage=$live.Name;StageSeconds=$live.Seconds;Pid=$live.Pid;ObservedPeakWorkingSet=$live.PeakWorkingSet;ObservedPeakWalBytes=$live.PeakWalBytes;CompleteBackup=(Test-Path -LiteralPath (Join-Path $case.CaseRoot 'complete-before-expiry.db.recovery/COMPLETED.json'));AvailableDBytes=(Get-PSDrive D).Free;ReadTransferBytes=$observedProcess.ReadTransferCount;WriteTransferBytes=$observedProcess.WriteTransferCount;KernelTime100ns=$observedProcess.KernelModeTime;UserTime100ns=$observedProcess.UserModeTime;Measurement='Supplementary ordinary-token OS metadata and existing live receipt; no database access or controller mutation'}
  $point|ConvertTo-Json -Compress|Add-Content -LiteralPath $observations -Encoding utf8NoBOM
  [Console]::WriteLine(('{0} {1} {2:F1}s backup={3} status={4}' -f $point.AtUtc,$point.Stage,$point.StageSeconds,$point.CompleteBackup,$point.WholeStatus))
  if($run.Status -ne 'InProgress'){break}
 }catch{
  [ordered]@{AtUtc=[DateTimeOffset]::UtcNow.ToString('o');ObservationError=$_.Exception.Message;Qualification='Observation failed; not a product result'}|ConvertTo-Json -Compress|Add-Content -LiteralPath $observations -Encoding utf8NoBOM
  [Console]::WriteLine('Observation error recorded: '+$_.Exception.Message)
 }
 Start-Sleep -Seconds 30
}
