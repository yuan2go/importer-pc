param([Parameter(Mandatory)][string]$Workspace,[ValidateSet('All','Core','Native')][string]$Section='All')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $Workspace
. ./scripts/project-environment.ps1
$closureSource=(& git rev-parse HEAD).Trim()
if ((& git status --porcelain)) { throw 'Verification requires clean committed source' }
$closureEvidence=Join-Path $Workspace ('.artifacts/camera-closure-final-'+$closureSource.Substring(0,8))
New-Item -ItemType Directory -Force -Path $closureEvidence | Out-Null
if ($Section -eq 'Native') {
 $closureReport=Get-Content (Join-Path $closureEvidence summary.json) -Raw | ConvertFrom-Json
 if ($closureReport.source_commit -ne $closureSource) {throw 'Cannot reuse another source verification'}
 if (@($closureReport.suites | Where-Object kind -eq 'WindowsNativeWpf').Count) {throw 'Existing native attempt must be archived before a controlled rerun'}
} else {
 $closureReport=[pscustomobject]@{source_commit=$closureSource;baseline_main='f2055ace6361d05387ddade133c4021e7efd6d0f';builds=@();suites=@();scope='Actual ZXing/Application/SQLite/built-in RCS loopback, Windows native WPF; synthetic native camera API. Physical camera/customer RCS/PDA/audio/site NOT_RUN'}
}
function Save-ClosureReport { $closureReport | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $closureEvidence summary.json) -Encoding utf8 }
if ($Section -ne 'Native') {
 foreach ($closureProject in @('tests/Ipc.Desktop.UiVerification','tests/Ipc.Application.Verification','src/Ipc.Desktop')) {
  $closureName=Split-Path -Leaf $closureProject
  $closureLog=Join-Path $closureEvidence ('build-'+$closureName+'.log')
  $closureArgs=@('build',$closureProject,'-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--nologo','--verbosity','quiet')
  if ($closureName -eq 'Ipc.Desktop') {$closureArgs+='-p:BusinessModule=Sportmaster'}
  & dotnet @closureArgs *> $closureLog
  $closureExit=$LASTEXITCODE
  $closureReport.builds+=@{project=$closureProject;exit_code=$closureExit;log=Split-Path -Leaf $closureLog}; Save-ClosureReport
  "BUILD $closureName exit=$closureExit"
  if ($closureExit) {Get-Content $closureLog -Tail 16;throw 'Build failed'}
 }
 $closureApp=(Resolve-Path tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll).Path
 foreach ($closureFlag in @('--camera-chain-closure','--camera-chain-reliability','--manual-supply','--rcs-timeout-errors','--sm-original-frame','--mvs-software-flow','--shutdown-resources','--sportmaster-rcs','--camera-rounds','--hikrobot-sdk-editing','--hikrobot-hardening','--ux-commercial-configuration','--p0-cleanup')) {
  $closureLog=Join-Path $closureEvidence ('application-'+$closureFlag.Substring(2)+'.log')
  & dotnet $closureApp $Workspace $closureFlag *> $closureLog
  $closureExit=$LASTEXITCODE; $closurePass=@(Select-String -LiteralPath $closureLog -Pattern '^PASS ').Count
  $closureReport.suites+=@{kind='Application';flag=$closureFlag;exit_code=$closureExit;pass_count=$closurePass;log=Split-Path -Leaf $closureLog}; Save-ClosureReport
  "APPLICATION $closureFlag exit=$closureExit pass=$closurePass"
  if ($closureExit) {Get-Content $closureLog -Tail 18;throw 'Application regression failed'}
 }
}
if ($Section -eq 'Core') { Save-ClosureReport; return }
$closureUi=(Resolve-Path tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll).Path
foreach ($closureFlag in @('--camera-chain-reliability-ui','--hikrobot-sdk-editing','--camera-round-ui','--camera-barcode-current','--operator-ux-18','--station-responsive-ui','--station-latency-ui','--presentation-ordering-ui','--workstation-barcode-ready','--manual-supply-ui','--continuous-pda-input','--operator-workspace','--camera-preview-lifecycle')) {
 [IO.File]::WriteAllText((Join-Path $Workspace '.artifacts/closure21-native-active.txt'),$closureFlag)
 & dotnet $closureUi $Workspace $closureFlag
 $closureExit=$LASTEXITCODE; $closureNative=Get-Content .artifacts/WINDOWS-WPF-EVIDENCE.txt
 $closureLog=Join-Path $closureEvidence ('native-'+$closureFlag.Substring(2)+'.log')
 Copy-Item -LiteralPath (Join-Path $closureNative WINDOWS-WPF.log) -Destination $closureLog
 $closurePass=@(Select-String -LiteralPath $closureLog -Pattern '^PASS ').Count
 $closureReport.suites+=@{kind='WindowsNativeWpf';flag=$closureFlag;exit_code=$closureExit;pass_count=$closurePass;log=Split-Path -Leaf $closureLog;native_folder=$closureNative}; Save-ClosureReport
 "NATIVE $closureFlag exit=$closureExit pass=$closurePass" | Tee-Object -FilePath (Join-Path $closureEvidence native-progress.log) -Append
 if ($closureExit) {[IO.File]::WriteAllText((Join-Path $Workspace '.artifacts/closure21-native-active.txt'),'FAILED '+$closureFlag);Get-Content $closureLog -Tail 18;throw 'Native guard/check failed; retain raw failure'}
}
$closureReport | Add-Member -NotePropertyName assertion_total -NotePropertyValue (($closureReport.suites | Measure-Object pass_count -Sum).Sum) -Force
Save-ClosureReport
[IO.File]::WriteAllText((Join-Path $Workspace '.artifacts/closure21-native-active.txt'),'COMPLETE')
"FINAL_SOURCE=$closureSource PASS_TOTAL=$($closureReport.assertion_total)"
