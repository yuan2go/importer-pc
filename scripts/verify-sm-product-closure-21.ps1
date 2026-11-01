param([ValidateSet('all','application','native','desktop')][string]$Phase='all')
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference='Stop'
. ./scripts/project-environment.ps1
$taskReports=[System.Collections.Generic.List[object]]::new()
$taskSource=(& git rev-parse HEAD).Trim()
if ((& git status --porcelain -- src tests scripts)) { throw 'Commit product/test/tool sources before frozen verification' }
$taskLogRoot=Join-Path (Get-Location) ('.artifacts/sm-product-closure-21/'+$taskSource)
$taskScope='SQLite/built-in loopback RCS/ZXing, Windows native WPF; physical MVS/customer RCS/target IPC/field/24x7 NOT_RUN'
New-Item -ItemType Directory -Path $taskLogRoot -Force | Out-Null
function Run-Check([string]$Name,[string[]]$Arguments,[bool]$Native=$false) {
 $taskLog=Join-Path $taskLogRoot ($Name+'.log')
 & dotnet @Arguments *> $taskLog
 $taskCode=$LASTEXITCODE
 if($Native -and (Test-Path .artifacts/WINDOWS-WPF-EVIDENCE.txt)) {
  $taskNative=Get-Content .artifacts/WINDOWS-WPF-EVIDENCE.txt -Raw
  $taskNative=$taskNative.Trim()
  Copy-Item -LiteralPath (Join-Path $taskNative 'WINDOWS-WPF.log') -Destination $taskLog -Force
  $taskReports.Add(@{name=$Name;exit=$taskCode;evidence=$taskNative;passes=@(Select-String -LiteralPath $taskLog -Pattern '^PASS').Count})
 }else{$taskReports.Add(@{name=$Name;exit=$taskCode;passes=@(Select-String -LiteralPath $taskLog -Pattern '^PASS').Count})}
 @{source_commit=$taskSource;scope=$taskScope;results=@($taskReports.ToArray())} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskLogRoot ($Phase+'-summary.json')) -Encoding utf8
 Write-Output "$Name exit=$taskCode"
 if($taskCode -ne 0){Get-Content -LiteralPath $taskLog -Tail 12}
}
if($Phase -in @('all','application')) {
 Run-Check 'build-application' @('build','tests/Ipc.Application.Verification','-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--verbosity','quiet')
 if($taskReports[-1].exit -eq 0){
  foreach($taskFlag in @('--product-closure-21','--storage-schema-only','--retention-ingress','--camera-chain-reliability','--manual-supply','--rcs-timeout-errors','--mvs-offline-activation','--shutdown-resources','--ux-commercial-configuration','--parameter-guidance-startup','--sportmaster-rcs','--mvs-software-flow','--sm-original-frame')) {
   Run-Check ('app'+$taskFlag) @('tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll','.', $taskFlag)
  }
 }
}
if($Phase -in @('all','native')) {
 Run-Check 'build-native' @('build','tests/Ipc.Desktop.UiVerification','-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--verbosity','quiet')
 if($taskReports[-1].exit -eq 0){
  foreach($taskFlag in @('--product-closure-21','--parameter-guidance-startup','--parameter-v3-only','--debug-log-live','--query-filters','--operator-ux-18','--camera-round-ui','--camera-chain-reliability-ui','--camera-preview-lifecycle')) {
   Run-Check ('native'+$taskFlag) @('tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll','.', $taskFlag) $true
  }
 }
}
if($Phase -in @('all','desktop')) {Run-Check 'build-desktop-sm' @('build','src/Ipc.Desktop','-c','Release','-p:BusinessModule=Sportmaster','-p:RestoreLockedMode=true','-m:1','-nr:false','--verbosity','quiet')}
Write-Output ('EVIDENCE='+$taskLogRoot)
if(@($taskReports|Where-Object exit -ne 0).Count){exit 1}
