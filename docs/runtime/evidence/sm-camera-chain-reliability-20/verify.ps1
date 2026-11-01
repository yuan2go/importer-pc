param([string]$Workspace)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $Workspace
. ./scripts/project-environment.ps1
$taskSource=(& git rev-parse HEAD).Trim()
if ((& git status --porcelain)) { throw 'Final verification requires clean committed source' }
$taskEvidence=Join-Path $Workspace ('.artifacts/camera-chain-final-'+$taskSource.Substring(0,8))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskReport=[ordered]@{source_commit=$taskSource;baseline_main='f2055ace';builds=@();suites=@();scope='Windows native WPF, actual ZXing/Application/SQLite and loopback built-in RCS; physical MVS/customer RCS/PDA/field installation NOT_RUN'}
function Save-TaskReport { $taskReport | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $taskEvidence summary.json) -Encoding utf8 }
foreach ($taskProject in @('tests/Ipc.Desktop.UiVerification','tests/Ipc.Application.Verification','src/Ipc.Desktop')) {
 $taskName=Split-Path -Leaf $taskProject
 $taskLog=Join-Path $taskEvidence ('build-'+$taskName+'.log')
 $taskArgs=@('build',$taskProject,'-c','Release','-p:RestoreLockedMode=true','-m:1','-nr:false','--nologo','--verbosity','quiet')
 if ($taskName -eq 'Ipc.Desktop') {$taskArgs+='-p:BusinessModule=Sportmaster'}
 & dotnet @taskArgs *> $taskLog
 $taskExit=$LASTEXITCODE
 $taskReport.builds+=@{project=$taskProject;exit_code=$taskExit;log=Split-Path -Leaf $taskLog};Save-TaskReport
 Write-Output "BUILD $taskName exit=$taskExit"
 if ($taskExit) {Get-Content $taskLog -Tail 20;throw 'Build failed'}
}
$taskApp=(Resolve-Path tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll).Path
foreach ($taskFlag in @('--camera-chain-reliability','--manual-supply','--rcs-timeout-errors','--sm-original-frame','--mvs-software-flow','--shutdown-resources','--sportmaster-rcs','--camera-rounds','--hikrobot-sdk-editing','--hikrobot-hardening','--ux-commercial-configuration')) {
 $taskLog=Join-Path $taskEvidence ('application-'+$taskFlag.Substring(2)+'.log')
 & dotnet $taskApp $Workspace $taskFlag *> $taskLog
 $taskExit=$LASTEXITCODE;$taskPass=@(Select-String -LiteralPath $taskLog -Pattern '^PASS ').Count
 $taskReport.suites+=@{kind='Application';flag=$taskFlag;exit_code=$taskExit;pass_count=$taskPass;log=Split-Path -Leaf $taskLog};Save-TaskReport
 Write-Output "APPLICATION $taskFlag exit=$taskExit pass=$taskPass"
 if ($taskExit) {Get-Content $taskLog -Tail 20;throw 'Application verification failed'}
}
$taskUi=(Resolve-Path tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll).Path
foreach ($taskFlag in @('--camera-chain-reliability-ui','--hikrobot-sdk-editing','--camera-round-ui','--camera-barcode-current','--operator-ux-18','--station-responsive-ui','--station-latency-ui','--presentation-ordering-ui','--workstation-barcode-ready','--manual-supply-ui','--continuous-pda-input','--operator-workspace','--camera-preview-lifecycle')) {
 & dotnet $taskUi $Workspace $taskFlag
 $taskExit=$LASTEXITCODE
 $taskNative=Get-Content .artifacts/WINDOWS-WPF-EVIDENCE.txt
 $taskRaw=Join-Path $taskNative WINDOWS-WPF.log
 $taskLog=Join-Path $taskEvidence ('native-'+$taskFlag.Substring(2)+'.log')
 Copy-Item -LiteralPath $taskRaw -Destination $taskLog
 $taskPass=@(Select-String -LiteralPath $taskLog -Pattern '^PASS ').Count
 $taskReport.suites+=@{kind='WindowsNativeWpf';flag=$taskFlag;exit_code=$taskExit;pass_count=$taskPass;log=Split-Path -Leaf $taskLog;native_folder=$taskNative};Save-TaskReport
 Write-Output "NATIVE $taskFlag exit=$taskExit pass=$taskPass"
 if ($taskExit) {Get-Content $taskLog -Tail 22;throw 'Native verification failed; keep original guard and raw failure'}
}
$taskReport.assertion_total=($taskReport.suites | Measure-Object -Property pass_count -Sum).Sum
Save-TaskReport
Write-Output "FINAL_EVIDENCE=$taskEvidence"
Write-Output "PASS_TOTAL=$($taskReport.assertion_total)"
