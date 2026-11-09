param([ValidateSet('Core','Native')][string]$Section='Core',[switch]$WalIntegration)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'project-environment.ps1')
$verificationRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $verificationRoot
if ((git status --porcelain)) {throw 'Verification requires a clean committed worktree'}
$sourceCommit=(git rev-parse HEAD).Trim()
$evidenceRoot=Join-Path $verificationRoot ('.artifacts/reader22-final-'+$sourceCommit.Substring(0,8))
New-Item -ItemType Directory -Force $evidenceRoot | Out-Null
$summaryFile=Join-Path $evidenceRoot ($Section.ToLowerInvariant()+'-summary.json')
if (Test-Path -LiteralPath $summaryFile) {throw 'Archive the prior attempt explicitly before a controlled rerun'}
$report=[ordered]@{source_commit=$sourceCommit;baseline_main='cf98e139fb31c0579f39617324f67175b00aec1f';section=$Section;wal_incremental=[bool]$WalIntegration;builds=@();suites=@();scope='Actual software/SQLite/ZXing/loopback RCS/native WPF with integrated PR175/177 custody and WAL guards; physical camera, customer RCS, PDA/audio hardware, installation and field endurance NOT_RUN'}
function Save-Report { $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryFile -Encoding utf8 }
if ($Section -eq 'Core') {
 foreach ($project in @('tests/Ipc.Application.Verification','tests/Ipc.Desktop.UiVerification','src/Ipc.Desktop')) {
  $buildLog=Join-Path $evidenceRoot ('build-'+(Split-Path -Leaf $project)+'.log')
  $buildArguments=@('build',$project,'-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--nologo','--verbosity','quiet')
  if ($project -eq 'src/Ipc.Desktop') {$buildArguments+='-p:BusinessModule=Sportmaster'}
  & dotnet @buildArguments *> $buildLog
  $buildExit=$LASTEXITCODE
  $report.builds+=@{project=$project;command='dotnet '+($buildArguments -join ' ');exit_code=$buildExit;log=Split-Path -Leaf $buildLog};Save-Report
  "BUILD $project exit=$buildExit"
  if ($buildExit) {Get-Content $buildLog -Tail 15;throw 'Release build failed'}
 }
 $testAssembly=Join-Path $verificationRoot 'tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll'
 $suiteFlags=@('--reader-ingress-terminal-22','--reader-feedback-22','--camera-chain-closure','--camera-chain-reliability','--manual-supply','--rcs-timeout-errors','--sm-original-frame','--shutdown-resources','--product-closure-21','--storage-schema-only')
 if ($WalIntegration) { $suiteFlags=@('--reader-ingress-terminal-22','--reader-feedback-22','--camera-chain-closure','--shutdown-resources','--storage-schema-only') }
} else {
 $coreSummary=Get-Content (Join-Path $evidenceRoot 'core-summary.json') -Raw | ConvertFrom-Json
 if ($coreSummary.source_commit -ne $sourceCommit -or @($coreSummary.builds).Count -ne 3 -or @($coreSummary.builds | Where-Object exit_code -ne 0).Count) {throw 'Same-source Release builds required'}
 $testAssembly=Join-Path $verificationRoot 'tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll'
 $suiteFlags=@('--reader-feedback-22-ui','--camera-chain-reliability-ui','--camera-barcode-current','--continuous-pda-input','--operator-workspace','--operator-ux-18','--presentation-ordering-ui','--hikrobot-sdk-editing','--product-closure-21')
 if ($WalIntegration) { $suiteFlags=@('--product-closure-21') }
}
foreach ($flag in $suiteFlags) {
 $suiteLog=Join-Path $evidenceRoot ($Section.ToLowerInvariant()+'-'+$flag.Substring(2)+'.log')
 & dotnet $testAssembly $verificationRoot $flag *> ($suiteLog+'.console')
 $suiteExit=$LASTEXITCODE
 $nativeDirectory=$null
 if ($Section -eq 'Native') {
  $nativeDirectory=Get-Content (Join-Path $verificationRoot '.artifacts/WINDOWS-WPF-EVIDENCE.txt')
  Copy-Item -LiteralPath (Join-Path $nativeDirectory 'WINDOWS-WPF.log') -Destination $suiteLog
 } else {Copy-Item -LiteralPath ($suiteLog+'.console') -Destination $suiteLog}
 $report.suites+=@{flag=$flag;command='dotnet '+$testAssembly+' '+$verificationRoot+' '+$flag;exit_code=$suiteExit;pass_count=@(Select-String -LiteralPath $suiteLog -Pattern '^PASS ').Count;log=Split-Path -Leaf $suiteLog;native_directory=$nativeDirectory};Save-Report
 "SUITE $Section $flag exit=$suiteExit pass=$($report.suites[-1].pass_count)"
 if ($suiteExit) {Get-Content $suiteLog -Tail 18;throw 'Regression failed; original log preserved'}
}
$report.assertion_total=($report.suites | Measure-Object pass_count -Sum).Sum
Save-Report
"COMPLETE $Section $sourceCommit PASS=$($report.assertion_total)"
