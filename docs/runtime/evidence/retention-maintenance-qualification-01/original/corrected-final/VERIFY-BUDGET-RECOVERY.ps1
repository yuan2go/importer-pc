#requires -Version 7.0
$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Actual standard-UAC elevated installer token required'}
$evidence='D:\projects\ipc-retention-full-scale-corrected-20261024'
$run=Get-Content (Join-Path $evidence 'PROTECTED-RESULT.json') -Raw|ConvertFrom-Json
if($run.Status -eq 'InProgress'){throw 'Wait for actual original maintenance terminal state; do not interrupt or extend it'}
if($run.SourceCommit -ne '5bb032d7354ef619064a015484a5001204edfff5'){throw 'Unexpected baseline source'}
$case=$run.Cases|Where-Object Count -eq 604800
if($case.CaseRoot -ne (Join-Path $run.ProtectedRoot 'case-604800')){throw 'Unexpected isolated synthetic case'}
$anchor=(Get-ItemProperty -LiteralPath HKLM:/SOFTWARE/ImporterPc/Storage).Root
if($anchor -ne 'D:\ImporterPc.Deployment' -or -not $run.ProtectedRoot.StartsWith($anchor+'\Qualification\retention-')){throw 'Original registered D anchor and qualification scope required'}
$backup=Join-Path $case.CaseRoot 'complete-before-expiry.db'
$restored=Join-Path $case.CaseRoot 'restored/Simulation/retention-qualification/sportmaster'
if(Test-Path -LiteralPath $restored){throw 'New isolated restore destination required'}
$manifest=Get-Content -LiteralPath ($backup+'.recovery/COMPLETED.json') -Raw|ConvertFrom-Json
if($manifest.DatabaseHash -ne '94F39CEC2C59E1BB9143761158059668DD0D86848A666A88578F07DA33F7C8DF'){throw 'Unexpected pre-retention complete backup identity'}
$cli=Join-Path $run.ProtectedRoot 'cli/Ipc.Assembly.exe'
if((Get-FileHash -LiteralPath (Join-Path $run.ProtectedRoot 'cli/Ipc.Application.ParcelFlow.dll')).Hash -ne $run.ProductSha256){throw 'Frozen product binary changed'}
$output=Join-Path $evidence 'POST-BUDGET-RECOVERY.json'
if(Test-Path -LiteralPath $output){throw 'Do not overwrite an earlier recovery receipt'}
Add-Type 'using System.Runtime.InteropServices; public static class BudgetRecoveryErrorMode { [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint value); }'
[BudgetRecoveryErrorMode]::SetErrorMode(3)|Out-Null
$result=[ordered]@{Status='InProgress';StartedUtc=[DateTimeOffset]::UtcNow.ToString('o');ActualOriginalMaintenanceStatus=$run.Status;SourceCommit=$run.SourceCommit;ProductSha256=$run.ProductSha256;ElevatedAdministrator=$true;BackupHash=$manifest.DatabaseHash;Destination=$restored;Runs=@();Scope='Separate actual CLI restore of complete pre-retention originals after the baseline terminal state. Does not turn failed maintenance into PASS, resume business or replay requests.'}
function Save-Recovery{$result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $output -Encoding utf8NoBOM}
function Invoke-RecoveryStep([string]$Name,[string[]]$Arguments){
 $entry=[ordered]@{Name=$Name;StartedUtc=[DateTimeOffset]::UtcNow.ToString('o');Executable=$cli;Arguments=$Arguments}
 $start=[Diagnostics.ProcessStartInfo]::new($cli);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
 $clock=[Diagnostics.Stopwatch]::StartNew();$process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 while(-not $process.WaitForExit(1000)){[ordered]@{Stage=$Name;Pid=$process.Id;Seconds=$clock.Elapsed.TotalSeconds;AtUtc=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $evidence 'RECOVERY-LIVE.json') -Encoding utf8NoBOM}
 $entry.ExitCode=$process.ExitCode;$entry.Seconds=$clock.Elapsed.TotalSeconds;$entry.EndedUtc=[DateTimeOffset]::UtcNow.ToString('o')
 [IO.File]::WriteAllText((Join-Path $evidence ($Name+'.out.log')),$stdout.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $evidence ($Name+'.err.log')),$stderr.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
 $process.Dispose();$result.Runs+=,$entry;Save-Recovery
 if($entry.ExitCode -ne 0){throw "Actual recovery CLI failed: $Name"}
}
try{
 Save-Recovery
 Invoke-RecoveryStep 'post-budget-plan-604800' @('--action','recovery-plan','--file',$backup,'--destination',$restored)
 Invoke-RecoveryStep 'post-budget-restore-604800' @('--action','restore','--file',$backup,'--destination',$restored)
 Invoke-RecoveryStep 'post-budget-inspect-604800' @('--action','maintenance-inspect','--file',(Join-Path $restored 'sportmaster.db'))
 $facts=Get-Content (Join-Path $evidence 'post-budget-inspect-604800.out.log') -Raw|ConvertFrom-Json
 if($facts.Archived -ne 604806 -or $facts.PendingReceipts -ne 1){throw 'Original operation or unresolved receipt responsibility missing after restore'}
 $result.RestoredDatabaseSha256=(Get-FileHash -LiteralPath (Join-Path $restored 'sportmaster.db')).Hash
 if($result.RestoredDatabaseSha256 -ne $manifest.DatabaseHash){throw 'Reference restore bytes differ from complete original backup'}
 $result.RestorePlanCopyInspectSeconds=($result.Runs|Measure-Object -Property Seconds -Sum).Sum
 $result.Status='PASS_SEPARATE_ISOLATED_RECOVERY_DRILL_ONLY'
}catch{$result.Status='FAIL_RECOVERY';$result.Error=$_.Exception.ToString();throw}
finally{$result.EndedUtc=[DateTimeOffset]::UtcNow.ToString('o');Save-Recovery}
