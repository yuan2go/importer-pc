$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$root='D:/projects/ipc-retention-ingress-full-retest-20261025'
$resultFile=Join-Path $root 'protected/PROTECTED-RESULT.json'
$liveFile=Join-Path $root 'protected/LIVE.json'
$output=Join-Path $root 'RUN-OBSERVATIONS.jsonl'
if(Test-Path -LiteralPath $output){throw 'New observation log required'}
$clock=[Diagnostics.Stopwatch]::StartNew();$last=[DateTimeOffset]::MinValue;$lastStage=''
while($clock.Elapsed.TotalHours -lt 4){
 try{
  $run=Get-Content -LiteralPath $resultFile -Raw|ConvertFrom-Json
  if($run.SourceCommit -ne 'b83998f18c56df2dd965630b25908feedc00a56f' -or $run.ProtectedRoot -ne 'D:\ImporterPc.Deployment\Qualification\retention-94f8bd49d7cd4f768117480983e66d2b'){throw 'Unexpected observed synthetic run'}
  $live=Get-Content -LiteralPath $liveFile -Raw|ConvertFrom-Json
  $now=[DateTimeOffset]::UtcNow
  $terminal=$run.Status -ne 'InProgress' -and $null -ne $run.EndedUtc
  if($terminal -or $live.Name -ne $lastStage -or ($now-$last).TotalSeconds -ge 60){
   $backup=Join-Path $run.ProtectedRoot 'case-604800/complete-before-expiry.db'
   $complete=Join-Path ($backup+'.recovery') 'COMPLETED.json'
   $observed=[ordered]@{AtUtc=$now.ToString('o');RunStatus=$run.Status;LiveStage=$live.Name;LivePid=$live.Pid;LiveSeconds=$live.Seconds;ObservedPeakWorkingSet=$live.PeakWorkingSet;ObservedPeakWalBytes=$live.PeakWalBytes;FreeDBytes=(Get-PSDrive D).Free;FullBackupBytes=if(Test-Path -LiteralPath $backup){(Get-Item -LiteralPath $backup).Length}else{$null};FullBackupCompleted=Test-Path -LiteralPath $complete;NoDatabaseConnectionOpened=$true}
   if(Test-Path -LiteralPath $complete){$observed.BackupMarkerLastWriteUtc=(Get-Item -LiteralPath $complete).LastWriteTimeUtc.ToString('o')}
   $observed|ConvertTo-Json -Compress|Add-Content -LiteralPath $output -Encoding utf8NoBOM
   $last=$now;$lastStage=$live.Name
  }
  if($terminal){exit 0}
 }catch{
  [ordered]@{AtUtc=[DateTimeOffset]::UtcNow.ToString('o');ObservationError=$_.Exception.Message;NoDatabaseConnectionOpened=$true;Scope='Observation error is not a runtime PASS or FAIL'}|ConvertTo-Json -Compress|Add-Content -LiteralPath $output -Encoding utf8NoBOM
 }
 Start-Sleep -Seconds 15
}
[ordered]@{AtUtc=[DateTimeOffset]::UtcNow.ToString('o');Status='OBSERVER_LIMIT_ONLY';MaintenanceWasNotStopped=$true}|ConvertTo-Json -Compress|Add-Content -LiteralPath $output -Encoding utf8NoBOM
