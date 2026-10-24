#requires -Version 5.1
<#
.SYNOPSIS
Read-only, external observation of ONE already-running isolated importer-pc process.
.DESCRIPTION
Does not launch, stop, restart or reconfigure the application. WM_NULL is a coarse
window responsiveness proxy, NOT an exact WPF Dispatcher or business measurement.
This collector can NEVER produce a business or 24x7 PASS. Restart the collector in
a NEW evidence directory for each new process instance; interrupted segments must
not be joined into a continuous run. Windows execution of this delivered script:
NOT_RUN during the Linux audit.
.EXAMPLE
.\Collect-Win11Stability.ps1 -TargetProcessId 1234 -DataRoot D:\isolated\sm-data -OutputDirectory D:\audits\sm-24h-01 -DurationHours 24
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$TargetProcessId,
    [Parameter(Mandatory=$true)][string]$DataRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(0.001,168)][double]$DurationHours = 24,
    [ValidateRange(1,60)][int]$SampleSeconds = 1,
    [ValidateRange(30,3600)][int]$StorageSampleSeconds = 60,
    [ValidateRange(50,1000)][int]$WindowTimeoutMilliseconds = 200,
    [ValidateRange(1,30)][int]$StorageTimeoutSeconds = 5,
    [ValidateRange(100,200000)][int]$MaximumScanFiles = 20000,
    [long]$TargetWindowHandle = 0,
    [string]$ExpectedSourceCommit = '',
    [string]$Combination = 'UNSPECIFIED'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'NOT_RUN: this collector requires Windows.' }
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($DataRoot.StartsWith('\\') -or $OutputDirectory.StartsWith('\\')) { throw 'Use local, isolated storage; network shares are outside this collector scope.' }
if (-not (Test-Path -LiteralPath $DataRoot -PathType Container)) { throw 'DataRoot does not exist. This collector never initializes application data.' }
if ($OutputDirectory.TrimEnd('\').Equals($DataRoot.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory.StartsWith($DataRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside application data.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Evidence directory already exists; choose a new directory to preserve previous evidence.' }
$target = [Diagnostics.Process]::GetProcessById($TargetProcessId)
$initialStarted = $target.StartTime.ToUniversalTime()
$exe = $target.MainModule.FileName
if ($target.HasExited) { $target.Dispose(); throw 'Target process already exited.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
if (-not ('IpcAuditNative' -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IpcAuditNative {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
  [StructLayout(LayoutKind.Sequential)] public struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetProcessIoCounters(IntPtr process, out IoCounters value);
}
'@
}
$utf8 = New-Object Text.UTF8Encoding($false)
$events = New-Object IO.StreamWriter((Join-Path $OutputDirectory 'events.jsonl'),$false,$utf8)
$samples = New-Object IO.StreamWriter((Join-Path $OutputDirectory 'process.csv'),$false,$utf8)
$storage = New-Object IO.StreamWriter((Join-Path $OutputDirectory 'storage.jsonl'),$false,$utf8)
$events.AutoFlush = $true; $samples.AutoFlush = $true; $storage.AutoFlush = $true
function Write-Event([string]$Kind, $Detail) {
    $events.WriteLine((ConvertTo-Json -Compress -Depth 8 -InputObject ([ordered]@{Utc=[DateTime]::UtcNow.ToString('o'); Kind=$Kind; Detail=$Detail})))
}
function Write-JsonFile([string]$Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name),(ConvertTo-Json -Depth 12 -InputObject $Value),$utf8)
}
function Percentile($Values, [double]$Fraction) {
    if ($Values.Count -eq 0) { return $null }
    $ordered = $Values.ToArray(); [Array]::Sort($ordered)
    return $ordered[[Math]::Min($ordered.Length-1,[Math]::Max(0,[int][Math]::Ceiling($ordered.Length*$Fraction)-1))]
}
$storageWorker = {
    param($Root,$FileLimit,$BudgetSeconds)
    $ErrorActionPreference='Stop'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $result=[ordered]@{Utc=[DateTime]::UtcNow.ToString('o'); Status='COMPLETE'; Files=0; Directories=0; TotalBytes=0L; DatabaseBytes=0L; WalBytes=0L; LogBytes=0L; FreeBytes=$null; SkippedReparsePoints=0; Errors=@(); Note='Metadata totals only; bounded scan may be partial. Extension classification is not an authoritative business-data inventory.'}
    try {
        $disk=New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Root)); $result.FreeBytes=$disk.AvailableFreeSpace
        $queue=New-Object 'Collections.Generic.Queue[string]'; $queue.Enqueue($Root)
        while($queue.Count -gt 0) {
            if($watch.Elapsed.TotalSeconds -gt $BudgetSeconds -or $result.Directories -ge 4000 -or $result.Files -ge $FileLimit) { $result.Status='PARTIAL_BUDGET'; break }
            $directory=$queue.Dequeue(); $result.Directories++
            $iterator=[IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator()
            try {
                while($iterator.MoveNext()) {
                    if($watch.Elapsed.TotalSeconds -gt $BudgetSeconds -or $result.Files -ge $FileLimit) { $result.Status='PARTIAL_BUDGET'; break }
                    $path=$iterator.Current; $attr=[IO.File]::GetAttributes($path)
                    if(($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $result.SkippedReparsePoints++; continue }
                    if(($attr -band [IO.FileAttributes]::Directory) -ne 0) { if($queue.Count -lt 4000) {$queue.Enqueue($path)} else {$result.Status='PARTIAL_DIRECTORY_BUDGET'}; continue }
                    $info=New-Object IO.FileInfo($path); $size=$info.Length
                    $result.Files++; $result.TotalBytes+=$size
                    if($path.EndsWith('-wal',[StringComparison]::OrdinalIgnoreCase)) {$result.WalBytes+=$size}
                    elseif($info.Extension -in '.db','.sqlite','.sqlite3') {$result.DatabaseBytes+=$size}
                    if($info.Name -like 'runtime.jsonl*' -or $info.Extension -eq '.log') {$result.LogBytes+=$size}
                }
            } finally { $iterator.Dispose() }
            if($result.Status -eq 'PARTIAL_BUDGET') { break }
        }
    } catch { $result.Status='PARTIAL_ERROR'; $result.Errors=@($_.Exception.GetType().FullName+': '+$_.Exception.Message) }
    $result['ElapsedMs']=$watch.Elapsed.TotalMilliseconds
    [pscustomobject]$result
}
$scanJob=$null; $scanStarted=$null; $scanSuppressed=$false; $scanTimedOut=$false
$uiTimes=New-Object 'Collections.Generic.List[double]'
$startUtc=[DateTime]::UtcNow; $watch=[Diagnostics.Stopwatch]::StartNew()
$endReason='COLLECTOR_INTERRUPTED'; $failureText=$null
$sampleCount=0; $uiSuccess=0; $uiFailures=0; $noWindow=0; $lastUiFailure=$false
$longestFailureStreak=0.0; $failureStreakStarted=$null
$maxSampleGap=0.0; $lastSample=0.0; $previousCpu=$null; $previousCpuAt=0.0
$maxPrivate=0L; $minPrivate=[long]::MaxValue; $maxHandles=0; $maxThreads=0
$nextStorage=0.0; $nextSample=0.0
try {
    # Inventory is best effort; failure is visible and never interpreted as qualification.
    $inventory=[ordered]@{Scope='ONE_PROCESS_EXTERNAL_READ_ONLY'; ScriptWindowsValidation='NOT_RUN_AT_DELIVERY'; CollectorPowerShell=$PSVersionTable.PSVersion.ToString(); ExpectedSourceCommit=$ExpectedSourceCommit; Combination=$Combination; TargetProcessId=$TargetProcessId; TargetStartedUtc=$initialStarted.ToString('o'); Executable=$exe; ExecutableSha256=$null; ApplicationRelease=$null; ApplicationReleaseSha256=$null; DataRoot=$DataRoot; OutputDirectory=$OutputDirectory; CommandLine=$MyInvocation.Line; Parameters=$PSBoundParameters; OS=$null; Cpu=$null; MemoryBytes=$null; InstalledDotnet=@(); ProcessNativeRuntimes=@(); InventoryErrors=@(); NotMeasured=@('Managed heap','GC pause','Exact Dispatcher delay','Input/admitted/rejected/completed/unresolved conservation','Business result latency','Queue depth/oldest age','Disk I/O latency','Actual power policy changes')}
    try {$inventory.ExecutableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash} catch {$inventory.InventoryErrors+=@('Executable hash: '+$_.Exception.Message)}
    try {
        $manifest=Join-Path ([IO.Path]::GetDirectoryName($exe)) 'application-release.json'
        if(Test-Path -LiteralPath $manifest) {
            if((New-Object IO.FileInfo($manifest)).Length -gt 2097152){throw 'Release manifest exceeds 2 MiB inventory budget.'}
            $inventory.ApplicationRelease=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
            $inventory.ApplicationReleaseSha256=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
        }
    } catch {$inventory.InventoryErrors+=@('Release identity: '+$_.Exception.Message)}
    try {$os=Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5; $inventory.OS=$os | Select-Object Caption,Version,BuildNumber,OSArchitecture,LastBootUpTime; $inventory.MemoryBytes=[long]$os.TotalVisibleMemorySize*1024} catch {$inventory.InventoryErrors+=@('OS inventory: '+$_.Exception.Message)}
    try {$inventory.Cpu=@(Get-CimInstance Win32_Processor -OperationTimeoutSec 5 | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors)} catch {$inventory.InventoryErrors+=@('CPU inventory: '+$_.Exception.Message)}
    try {
        foreach($root in @('HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx','HKLM:\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx')) {
            if(Test-Path $root) { foreach($key in Get-ChildItem $root) {$inventory.InstalledDotnet+=@([ordered]@{Key=$key.Name;Versions=@($key.GetValueNames())})} }
        }
        $inventory.ProcessNativeRuntimes=@($target.Modules | Where-Object {$_.ModuleName -in 'coreclr.dll','clr.dll','wpfgfx_cor3.dll','e_sqlite3.dll' -or $_.ModuleName -match 'Hik|MvCodeReader'} | ForEach-Object {[ordered]@{Name=$_.ModuleName;Path=$_.FileName;Version=$_.FileVersionInfo.FileVersion}})
    } catch {$inventory.InventoryErrors+=@('Runtime inventory: '+$_.Exception.Message)}
    Write-JsonFile 'environment.json' $inventory
    # Start the observation duration after inventory; initial process identity stays fixed.
    $startUtc=[DateTime]::UtcNow; $watch.Restart()
    Write-Event 'SEGMENT_START' ([ordered]@{ProcessId=$TargetProcessId;StartedUtc=$initialStarted.ToString('o');RequestedHours=$DurationHours;NoRestartIsPerformed=$true})
    $samples.WriteLine('Utc,ElapsedSeconds,SampleGapSeconds,ProcessId,PrivateBytes,WorkingSetBytes,CpuOneCorePercent,CpuMachinePercent,Threads,Handles,GdiObjects,UserObjects,ReadBytesTotal,WriteBytesTotal,WindowHandle,UiProbeStatus,UiProxyLatencyMs,Win32Error')
    while($watch.Elapsed.TotalHours -lt $DurationHours) {
        $now=$watch.Elapsed.TotalSeconds
        if($now -lt $nextSample) {Start-Sleep -Milliseconds ([int][Math]::Min(500,[Math]::Max(1,($nextSample-$now)*1000))); continue}
        if($scanJob -ne $null) {
            if($scanJob.State -in 'Completed','Failed','Stopped') {
                try {foreach($row in Receive-Job -Job $scanJob -ErrorAction Stop) {$storage.WriteLine((ConvertTo-Json -Depth 8 -Compress -InputObject $row))}} catch {Write-Event 'STORAGE_SAMPLE_ERROR' $_.Exception.Message}
                Remove-Job -Job $scanJob -Force; $scanJob=$null
            } elseif(-not $scanTimedOut -and ($now-$scanStarted) -gt $StorageTimeoutSeconds+3) {
                # Stop-Job itself may wait for a blocked OS filesystem call. Never
                # move that wait onto the process/UI sampling lane or launch more jobs.
                $scanSuppressed=$true; $scanTimedOut=$true
                Write-Event 'STORAGE_SAMPLE_TIMEOUT' ([ordered]@{JobId=$scanJob.Id;Action='Disable all further metadata scans; retain the single pending collector-owned job without awaiting Stop-Job';Limitation='A blocked OS call is not claimed to be cancelled. No application process is stopped.'})
            }
        }
        $target.Refresh()
        if($target.HasExited) {Write-Event 'PROCESS_EXIT' ([ordered]@{ExitCode=$target.ExitCode;NewRunRequired=$true}); $endReason='PROCESS_EXIT'; break}
        if($target.StartTime.ToUniversalTime() -ne $initialStarted) {Write-Event 'PROCESS_IDENTITY_CHANGED' 'PID was reused. Do not merge the replacement process into this segment.'; $endReason='PROCESS_IDENTITY_CHANGED'; break}
        $gap=if($sampleCount -eq 0){0.0}else{$now-$lastSample}; $maxSampleGap=[Math]::Max($maxSampleGap,$gap); $lastSample=$now
        $cpu=$target.TotalProcessorTime.TotalMilliseconds; $cpuPercent=$null
        if($previousCpu -ne $null -and $now -gt $previousCpuAt) {$cpuPercent=100*($cpu-$previousCpu)/(1000*($now-$previousCpuAt))}
        $previousCpu=$cpu; $previousCpuAt=$now
        $private=$target.PrivateMemorySize64; $maxPrivate=[Math]::Max($maxPrivate,$private); $minPrivate=[Math]::Min($minPrivate,$private)
        $threads=$target.Threads.Count; $handles=$target.HandleCount; $maxThreads=[Math]::Max($maxThreads,$threads); $maxHandles=[Math]::Max($maxHandles,$handles)
        $io=New-Object IpcAuditNative+IoCounters; $ioOk=[IpcAuditNative]::GetProcessIoCounters($target.Handle,[ref]$io)
        $hwnd=if($TargetWindowHandle -eq 0){$target.MainWindowHandle}else{[IntPtr]$TargetWindowHandle}
        $probe='NO_WINDOW'; $latency=$null; $nativeError=0
        if($hwnd -ne [IntPtr]::Zero) {
            $windowOwner=[uint32]0; [void][IpcAuditNative]::GetWindowThreadProcessId($hwnd,[ref]$windowOwner)
            if($windowOwner -ne $TargetProcessId){Write-Event 'WINDOW_OWNER_MISMATCH' @{Window=$hwnd.ToInt64();ObservedProcessId=$windowOwner}; $hwnd=[IntPtr]::Zero}
        }
        if($hwnd -ne [IntPtr]::Zero) {
            $answer=[UIntPtr]::Zero; $probeWatch=[Diagnostics.Stopwatch]::StartNew()
            $ok=[IpcAuditNative]::SendMessageTimeout($hwnd,0,[UIntPtr]::Zero,[IntPtr]::Zero,35,[uint32]$WindowTimeoutMilliseconds,[ref]$answer)
            $nativeError=[Runtime.InteropServices.Marshal]::GetLastWin32Error(); $latency=$probeWatch.Elapsed.TotalMilliseconds
            if($ok -eq [IntPtr]::Zero) {$probe='TIMEOUT_OR_FAILURE'; $uiFailures++; if($failureStreakStarted -eq $null){$failureStreakStarted=$now}; if(-not $lastUiFailure){Write-Event 'UI_PROXY_FAILURE_STARTED' @{Win32Error=$nativeError;Window=$hwnd.ToInt64()}}; $lastUiFailure=$true}
            else {$probe='RETURNED'; $nativeError=0; $uiSuccess++; $uiTimes.Add($latency); if($lastUiFailure){Write-Event 'UI_PROXY_RECOVERED' @{ObservedFailureSpanSeconds=($now-$failureStreakStarted)}}; if($failureStreakStarted -ne $null){$longestFailureStreak=[Math]::Max($longestFailureStreak,$now-$failureStreakStarted)}; $failureStreakStarted=$null; $lastUiFailure=$false}
        } else {$noWindow++; if($failureStreakStarted -ne $null){$longestFailureStreak=[Math]::Max($longestFailureStreak,$now-$failureStreakStarted)}; $failureStreakStarted=$null; $lastUiFailure=$false}
        $fields=@([DateTime]::UtcNow.ToString('o'),$now,$gap,$TargetProcessId,$private,$target.WorkingSet64,$cpuPercent,$(if($cpuPercent -ne $null){$cpuPercent/[Environment]::ProcessorCount}else{$null}),$threads,$handles,[IpcAuditNative]::GetGuiResources($target.Handle,0),[IpcAuditNative]::GetGuiResources($target.Handle,1),$(if($ioOk){$io.ReadBytes}else{$null}),$(if($ioOk){$io.WriteBytes}else{$null}),$hwnd.ToInt64(),$probe,$latency,$nativeError)
        $samples.WriteLine(($fields | ForEach-Object {if($_ -is [IFormattable]){$_.ToString($null,[Globalization.CultureInfo]::InvariantCulture)}else{[string]$_}}) -join ',')
        $sampleCount++
        if(-not $scanSuppressed -and $scanJob -eq $null -and $now -ge $nextStorage) {$scanJob=Start-Job -ScriptBlock $storageWorker -ArgumentList $DataRoot,$MaximumScanFiles,$StorageTimeoutSeconds; $scanStarted=$now; $nextStorage=$now+$StorageSampleSeconds}
        $nextSample=$now+$SampleSeconds
    }
    if($endReason -eq 'COLLECTOR_INTERRUPTED') {$endReason='REQUESTED_DURATION_REACHED'}
} catch {
    $endReason='COLLECTOR_ERROR'; $failureText=$_.Exception.ToString()
    # An exit may race a metrics property read. Preserve that distinction.
    try {if($target.HasExited){$endReason='PROCESS_EXIT';Write-Event 'PROCESS_EXIT' ([ordered]@{ExitCode=$target.ExitCode;MetricReadError=$failureText;NewRunRequired=$true})}} catch { }
    if($endReason -eq 'COLLECTOR_ERROR'){Write-Event 'COLLECTOR_ERROR' $failureText}
} finally {
    $actual=$watch.Elapsed.TotalSeconds
    if($scanJob -ne $null) {
        if($scanJob.State -in 'Completed','Failed','Stopped') {
            try {foreach($row in Receive-Job -Job $scanJob -ErrorAction Stop) {$storage.WriteLine((ConvertTo-Json -Depth 8 -Compress -InputObject $row))}; Remove-Job -Job $scanJob -Force} catch {Write-Event 'COLLECTOR_JOB_CLEANUP_ERROR' $_.Exception.Message}
        } else {
            $scanSuppressed=$true
            Write-Event 'COLLECTOR_METADATA_JOB_STILL_PENDING' ([ordered]@{JobId=$scanJob.Id;State=[string]$scanJob.State;Action='Do not synchronously stop/wait. Main collector finishes; retain at most this one job in the PowerShell session until its OS call returns.';Instruction='Record this evidence gap. Do not start additional metadata collectors against the same stalled volume.'})
        }
    }
    if($failureStreakStarted -ne $null){$longestFailureStreak=[Math]::Max($longestFailureStreak,$actual-$failureStreakStarted)}
    $summary=[ordered]@{Conclusion='EVIDENCE_ONLY_NOT_A_PASS'; SegmentEnd=$endReason; StartedUtc=$startUtc.ToString('o'); EndedUtc=[DateTime]::UtcNow.ToString('o'); ActualSeconds=$actual; RequestedSeconds=$DurationHours*3600; Samples=$sampleCount; TargetProcessId=$TargetProcessId; TargetStartedUtc=$initialStarted.ToString('o'); SourceCommitClaim=$ExpectedSourceCommit; CombinationClaim=$Combination; StorageScansSuppressed=$scanSuppressed; UiProxy=@{Returned=$uiSuccess;TimeoutOrFailure=$uiFailures;NoWindow=$noWindow;P95Ms=(Percentile $uiTimes 0.95);P99Ms=(Percentile $uiTimes 0.99);MaxMs=$(if($uiTimes.Count){($uiTimes | Measure-Object -Maximum).Maximum}else{$null});LongestObservedFailureSpanSeconds=$longestFailureStreak}; MaxSamplingGapSeconds=$maxSampleGap; PrivateBytesMin=$(if($sampleCount){$minPrivate}else{$null}); PrivateBytesMax=$maxPrivate; HandlesMax=$maxHandles; ThreadsMax=$maxThreads; CollectorError=$failureText; BusinessVerification='NOT_MEASURED'; ExactDispatcherDelay='NOT_MEASURED'; ManagedHeapAndGc='NOT_MEASURED'; RestartPolicy='New PID/start-time requires a new output directory and a separately counted segment'; Interpretation='WM_NULL success is neither rendered-frame proof nor business health. No-memory-leak conclusion requires warmup-adjusted trends and managed/native diagnostics. Full 24h continuous load, business conservation, correct driver/SDK identity and recovery trials must be independently joined by time and request identity.'}
    try {Write-JsonFile 'summary.json' $summary; Write-Event 'SEGMENT_END' $summary} finally {$events.Dispose();$samples.Dispose();$storage.Dispose();$target.Dispose()}
}
Write-Output (Join-Path $OutputDirectory 'summary.json')
if($endReason -eq 'COLLECTOR_ERROR') {exit 2}
if($endReason -in 'PROCESS_EXIT','PROCESS_IDENTITY_CHANGED') {exit 3}
