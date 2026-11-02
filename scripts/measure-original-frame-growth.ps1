param(
 [Parameter(Mandatory)][string]$OriginalFramesDirectory,
 [ValidateRange(0.01,120)][double]$DurationMinutes=30,
 [ValidateRange(1,60)][int]$SampleSeconds=5,
 [ValidateRange(1,30)][int]$RetentionDays=7
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'project-environment.ps1')
$frameRoot=(Resolve-Path -LiteralPath $OriginalFramesDirectory).ProviderPath
if ($frameRoot -notmatch '^D:\\' -or -not (Test-Path -LiteralPath $frameRoot -PathType Container)) {throw '请指定D盘实际原帧目录'}
$frameCursor=Get-Item -LiteralPath $frameRoot
while ($null -ne $frameCursor) {
 if ($frameCursor.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw '原帧路径不能经过链接或重解析点'}
 $frameCursor=$frameCursor.Parent
}
function Read-FrameInventory {
 $inventory=[Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
 $directories=[Collections.Generic.Stack[string]]::new();$directories.Push($frameRoot)
 while ($directories.Count) {
  foreach ($item in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
   if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw '原帧目录包含链接或重解析点，停止测量'}
   if ($item.PSIsContainer) {$directories.Push($item.FullName);continue}
   if ($item.Extension -ne '.png') {continue}
   if ($inventory.Count -ge 250000) {throw '原帧测量超过250000文件预算，停止；请缩小测量目录'}
   $inventory.Add($item.FullName,$item.Length)
  }
 }
 return ,$inventory
}
$previous=Read-FrameInventory
$initialBytes=($previous.Values | Measure-Object -Sum).Sum
$observedWrittenBytes=0L;$newFiles=0L;$samples=0
$measurementClock=[Diagnostics.Stopwatch]::StartNew()
while ($measurementClock.Elapsed.TotalMinutes -lt $DurationMinutes) {
 Start-Sleep -Milliseconds ([int][Math]::Max(1,[Math]::Min($SampleSeconds*1000,($DurationMinutes*60-$measurementClock.Elapsed.TotalSeconds)*1000)))
 $next=Read-FrameInventory
 foreach ($pair in $next.GetEnumerator()) {
  if (!$previous.ContainsKey($pair.Key)) {$newFiles++;$observedWrittenBytes+=$pair.Value}
  elseif ($pair.Value -gt $previous[$pair.Key]) {$observedWrittenBytes+=($pair.Value-$previous[$pair.Key])}
 }
 $previous=$next;$samples++
}
$seconds=$measurementClock.Elapsed.TotalSeconds
[ordered]@{
 Scope='ReadOnlySampledOriginalPngGrowth; not parcel count, disk endurance or field acceptance'
 SampleSeconds=[Math]::Round($seconds,3);Samples=$samples;InitialBytes=$initialBytes
 FinalBytes=($previous.Values | Measure-Object -Sum).Sum;ObservedNewFiles=$newFiles
 ObservedWrittenBytes=$observedWrittenBytes;ObservedBytesPerHour=[Math]::Round($observedWrittenBytes/$seconds*3600)
 RetentionDays=$RetentionDays;ProjectedOriginalBytes=[Math]::Round($observedWrittenBytes/$seconds*86400*$RetentionDays)
 Limit='Sampled lower bound: files created and removed between polls can be missed. Excludes database, backups, logs and disk reserve. No files deleted.'
} | ConvertTo-Json
