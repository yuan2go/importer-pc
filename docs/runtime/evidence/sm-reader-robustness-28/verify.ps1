param([string]$Workspace='D:/w/ipc-reader-robustness-28',[string]$Evidence='D:/ImporterPc.LocalValidation/reader28/final')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $Workspace
. ./scripts/project-environment.ps1
if(& git status --porcelain){throw 'Committed clean source required'}
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
$taskReport=[ordered]@{source_commit=(& git rev-parse HEAD).Trim();builds=@();suites=@();scope='Windows WPF, actual ZXing/SQLite and built-in loopback RCS, synthetic SDK frames; physical MVS/customer RCS/field NOT_RUN'}
function Save-TaskReport {$taskReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Evidence 'summary.json') -Encoding utf8}
foreach($taskProject in @('tests/Ipc.Application.Verification','tests/Ipc.Desktop.UiVerification','src/Ipc.Desktop')){
 $taskName=Split-Path -Leaf $taskProject;$taskLog=Join-Path $Evidence ('build-'+$taskName+'.log')
 $taskArgs=@('build',$taskProject,'-c','Release','-p:RestoreLockedMode=true','-p:NuGetAudit=false','-m:1','-nr:false','--nologo','-v:minimal')
 if($taskName -eq 'Ipc.Desktop'){$taskArgs+='-p:BusinessModule=Sportmaster'}
 & dotnet @taskArgs *> $taskLog
 $taskExit=$LASTEXITCODE;$taskReport.builds+=@{project=$taskProject;exit_code=$taskExit;log=Split-Path -Leaf $taskLog};Save-TaskReport
 Write-Output "BUILD $taskName exit=$taskExit"
 if($taskExit){Get-Content $taskLog -Tail 16;throw 'Build failed'}
}
$taskApp=Join-Path $Workspace 'tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll'
foreach($taskFlag in @('reader-robustness-28','mvs-contrast-checks', 'unread-package-27','camera-chain-reliability','camera-chain-closure','reader-feedback-22','reader-ingress-terminal-22','mvs-software-flow','sm-original-frame','shutdown-resources','camera-rounds')){
 $taskLog=Join-Path $Evidence ('app-'+$taskFlag+'.log')
 & dotnet $taskApp $Workspace ('--'+$taskFlag) *> $taskLog
 $taskExit=$LASTEXITCODE;$taskPass=@(Select-String -LiteralPath $taskLog -Pattern '^PASS ').Count
 $taskReport.suites+=@{kind='Application';flag=$taskFlag;exit_code=$taskExit;pass_count=$taskPass;log=Split-Path -Leaf $taskLog};Save-TaskReport
 Write-Output "APP $taskFlag exit=$taskExit PASS=$taskPass"
 if($taskExit){Get-Content $taskLog -Tail 16;throw 'Application suite failed'}
}
$taskUi=Join-Path $Workspace 'tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll'
foreach($taskFlag in @('reader-robustness-28-ui','unread-package-27-ui','camera-chain-reliability-ui','camera-barcode-current','station-responsive-ui','reader-feedback-22-ui')){
 & dotnet $taskUi $Workspace ('--'+$taskFlag) *> (Join-Path $Evidence ('console-'+$taskFlag+'.log'))
 $taskExit=$LASTEXITCODE;$taskNative=Get-Content .artifacts/WINDOWS-WPF-EVIDENCE.txt
 $taskLog=Join-Path $Evidence ('native-'+$taskFlag+'.log')
 Copy-Item -LiteralPath (Join-Path $taskNative 'WINDOWS-WPF.log') -Destination $taskLog
 if($taskFlag -eq 'reader-robustness-28-ui'){Get-ChildItem -LiteralPath $taskNative -Filter '*.png' | Copy-Item -Destination $Evidence}
 $taskPass=@(Select-String -LiteralPath $taskLog -Pattern '^PASS ').Count
 $taskReport.suites+=@{kind='WindowsNativeWpf';flag=$taskFlag;exit_code=$taskExit;pass_count=$taskPass;log=Split-Path -Leaf $taskLog;native_folder=$taskNative};Save-TaskReport
 Write-Output "NATIVE $taskFlag exit=$taskExit PASS=$taskPass"
 if($taskExit){Get-Content $taskLog -Tail 18;throw 'Native suite failed; preserve original failure'}
}
$taskPublish=Join-Path $Evidence 'publish'
& dotnet publish src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster --no-build --no-restore -o $taskPublish *> (Join-Path $Evidence 'publish.log')
$taskReport.publish_exit_code=$LASTEXITCODE;Save-TaskReport
if($LASTEXITCODE){throw 'Publish failed'}
$taskProcess=Start-Process -FilePath (Join-Path $taskPublish 'Ipc.Desktop.exe') -ArgumentList '--environment-report' -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $Evidence 'environment-report.json') -RedirectStandardError (Join-Path $Evidence 'environment-report.stderr.log')
$taskReport.smoke_exit_code=$taskProcess.ExitCode
$taskReport.product_version=[System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskPublish 'Ipc.Desktop.dll')).ProductVersion
$taskReport.assertions=($taskReport.suites | Measure-Object -Property pass_count -Sum).Sum
Save-TaskReport
if($taskProcess.ExitCode){throw 'Startup environment smoke failed'}
Write-Output "SMOKE exit=$($taskProcess.ExitCode) PASS_TOTAL=$($taskReport.assertions)"
