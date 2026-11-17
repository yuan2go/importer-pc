param([ValidateSet('Core','Native','Publish')][string]$Section='Core')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'project-environment.ps1')
. (Join-Path $PSScriptRoot 'verification-evidence.ps1')
$verificationRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $verificationRoot
if ((git status --porcelain)) {throw 'Verification requires a clean committed worktree'}
$sourceCommit=(git rev-parse HEAD).Trim()
$evidenceRoot=Join-Path $verificationRoot ('.artifacts/ui24-final-'+$sourceCommit.Substring(0,8))
New-Item -ItemType Directory -Force $evidenceRoot | Out-Null
$summaryFile=Join-Path $evidenceRoot ($Section.ToLowerInvariant()+'-summary.json')
if (Test-Path -LiteralPath $summaryFile) {throw 'Archive previous attempt explicitly before controlled rerun'}
$bundle=Join-Path $evidenceRoot 'frozen'
$attempt=Join-Path $evidenceRoot ($Section.ToLowerInvariant()+'-attempt.json')
if(Test-Path -LiteralPath $attempt){throw 'This attempt already started; retain its failure evidence'}
@{Source=$sourceCommit;StartedUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $attempt
$report=[ordered]@{source_commit=$sourceCommit;baseline_main='9284a32e5633df8c1ff0208c7a94e253328fe7a8';section=$Section;builds=@();suites=@();scope='Actual software/SQLite/ZXing/loopback RCS/native WPF. Synthetic deployment callbacks. Physical MVS/customer RCS/protected installation/field endurance NOT_RUN. CI disabled.'}
function Save-Report { $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryFile -Encoding utf8 }
function Require-Source { if ((git rev-parse HEAD).Trim() -ne $sourceCommit -or (git status --porcelain)) {throw 'Source changed during verification'} }
if ($Section -eq 'Core') {
 foreach ($project in @('tests/Ipc.Application.Verification','tests/Ipc.Desktop.UiVerification','src/Ipc.Desktop')) {
  $buildLog=Join-Path $evidenceRoot ('build-'+(Split-Path -Leaf $project)+'.log')
  $buildArguments=@('build',$project,'-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--nologo','--verbosity','quiet')
  if ($project -eq 'src/Ipc.Desktop') {$buildArguments+='-p:BusinessModule=Sportmaster'}
  & dotnet @buildArguments *> $buildLog
  $buildExit=$LASTEXITCODE
  $report.builds+=@{project=$project;command='dotnet '+($buildArguments -join ' ');exit_code=$buildExit;log=Split-Path -Leaf $buildLog};Save-Report
  "BUILD $project exit=$buildExit"
  if ($buildExit) {Get-Content $buildLog -Tail 18;throw 'Release build failed'}
 }
 $testAssembly=Join-Path $verificationRoot 'tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll'
 $suiteFlags=@('--ui-spec-24','--product-closure-21','--camera-rounds','--camera-chain-closure','--management-mode','--manual-supply','--rcs-timeout-errors','--reader-feedback-22','--parameter-guidance-startup','--storage-schema-only','--reliability-02','--shutdown-resources')
} else {
 $core=Get-Content (Join-Path $evidenceRoot 'core-summary.json') -Raw | ConvertFrom-Json
 if ($core.completed -ne $true -or $core.source_commit -ne $sourceCommit -or @($core.builds).Count -ne 3 -or @($core.builds | Where-Object exit_code -ne 0).Count -or @($core.suites | Where-Object exit_code -ne 0).Count -or $core.suites.Count -ne 12) {throw 'Same-source complete Core verification required'}
 Assert-VerificationBundle $bundle
 if((Get-FileHash -LiteralPath (Join-Path $bundle 'inventory.json')).Hash -ne $core.bundle_sha256){throw 'Core bundle identity differs'}
 $env:IPC_FROZEN_DESKTOP=Join-Path $bundle 'desktop/Ipc.Desktop.dll'
 $testAssembly=Join-Path $bundle 'native/Ipc.Desktop.UiVerification.dll'
 $suiteFlags=@('--ui-spec-24','--product-closure-21','--hikrobot-sdk-editing','--parameter-v3-only','--query-filters','--debug-log-live','--camera-round-ui','--camera-preview-lifecycle','--installer-first-run-ui','--uninstall-result-actions','--reader-feedback-22-ui','--operator-ux-18','--parameter-guidance-startup')
}
if ($Section -eq 'Publish') {
 $publishRoot=Join-Path $evidenceRoot 'publish'
 $publishArguments=@('publish','src/Ipc.Desktop','-c','Release','-p:BusinessModule=Sportmaster','-p:RestoreLockedMode=true','-m:1','-nr:false','--no-build','--no-restore','--self-contained','false','-o',$publishRoot)
 & dotnet @publishArguments *> (Join-Path $evidenceRoot 'publish.log');$publishExit=$LASTEXITCODE
 $report.publish=@{exit_code=$publishExit;status='Started'};Save-Report
 if ($publishExit) {throw 'Publish failed'}
 Assert-VerificationBundle $bundle
 foreach($file in Get-ChildItem -LiteralPath $publishRoot -File -Recurse){$relative=[IO.Path]::GetRelativePath($publishRoot,$file.FullName);$original=Join-Path (Join-Path $bundle 'desktop') $relative;if(!(Test-Path -LiteralPath $original) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $original).Hash){throw ('Published bytes differ from verified Core: '+$relative)}}
 Assert-PublishedBundle $publishRoot $bundle;Require-Source
 $executable=Join-Path $publishRoot 'Ipc.Desktop.exe'
 $process=Start-Process -FilePath $executable -ArgumentList '--environment-report' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidenceRoot 'environment.json') -RedirectStandardError (Join-Path $evidenceRoot 'environment-error.log')
 if (!$process.WaitForExit(30000)) {Stop-Process -Id $process.Id;throw 'Published EXE startup timed out'}
 $process.Refresh();$exit=$process.ExitCode
 $version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishRoot 'Ipc.Desktop.dll')).ProductVersion
 $report.publish=@{command='dotnet '+($publishArguments -join ' ');exit_code=$publishExit;launch_exit=$exit;informational_version=$version;desktop_sha256=(Get-FileHash (Join-Path $publishRoot 'Ipc.Desktop.dll') -Algorithm SHA256).Hash}
 Save-Report;Require-Source
 if ($exit -ne 0 -or !$version.EndsWith($sourceCommit)) {throw 'Published EXE did not confirm original source'}
 'PUBLISH startup exit=0 source='+$sourceCommit;return
}
foreach ($flag in $suiteFlags) {
 Require-Source
 if($Section -eq 'Native'){Assert-VerificationBundle $bundle;Reset-NativeEvidence $verificationRoot}
 $suiteLog=Join-Path $evidenceRoot ($Section.ToLowerInvariant()+'-'+$flag.Substring(2)+'.log')
 & dotnet $testAssembly $verificationRoot $flag *> ($suiteLog+'.console')
 $suiteExit=$LASTEXITCODE;$nativeDirectory=$null
 if ($Section -eq 'Native') {
  try{$nativeDirectory=Read-NativeEvidence $verificationRoot}catch{$report.suites+=@{flag=$flag;exit_code=$suiteExit;evidence_gap=$_.Exception.Message;console=$suiteLog+'.console'};Save-Report;throw}
  Copy-Item -LiteralPath (Join-Path $nativeDirectory 'WINDOWS-WPF.log') -Destination $suiteLog
 } else {Copy-Item -LiteralPath ($suiteLog+'.console') -Destination $suiteLog}
 $report.suites+=@{flag=$flag;command='dotnet '+$testAssembly+' '+$verificationRoot+' '+$flag;exit_code=$suiteExit;pass_count=@(Select-String -LiteralPath $suiteLog -Pattern '^PASS ').Count;log=Split-Path -Leaf $suiteLog;native_directory=$nativeDirectory};Save-Report
 "SUITE $Section $flag exit=$suiteExit pass=$($report.suites[-1].pass_count)"
 if ($suiteExit) {Get-Content $suiteLog -Tail 22;throw 'Regression failed; original log retained'}
}
$report.assertion_total=($report.suites | Measure-Object pass_count -Sum).Sum
Require-Source
if($Section -eq 'Core'){Save-VerificationBundle $verificationRoot $bundle;$report.bundle_sha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'inventory.json')).Hash;Require-Source}
$report.completed=$true;Save-Report
"COMPLETE $Section $sourceCommit PASS=$($report.assertion_total)"
