param([string]$Workspace)
Set-Location -LiteralPath $Workspace
. ./scripts/project-environment.ps1
$ErrorActionPreference='Stop'
$sourceCommit = (& git rev-parse HEAD).Trim()
if ((& git status --porcelain)) { throw 'Source must be clean before final verification' }
$taskEvidence = Join-Path $Workspace ('.artifacts/operator-ux-18-final-' + $sourceCommit.Substring(0,8))
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
$buildRows = @(); $suiteRows = @()
foreach ($project in @('tests/Ipc.Application.Verification/Ipc.Application.Verification.csproj','tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj','src/Ipc.Desktop/Ipc.Desktop.csproj')) {
 $projectName = [IO.Path]::GetFileNameWithoutExtension($project); $buildLog = Join-Path $taskEvidence ('build-' + $projectName + '.log')
 $buildArgs = @('build',$project,'-c','Release','--no-restore','-m:1','-nr:false','-v:q'); if ($projectName -eq 'Ipc.Desktop') {$buildArgs += '-p:BusinessModule=Sportmaster'} else {$buildArgs += '-t:Rebuild'}
 & dotnet @buildArgs 2>&1 | Out-File -LiteralPath $buildLog -Encoding utf8
 $buildExit = $LASTEXITCODE; $buildRows += @{project=$project;exit_code=$buildExit;log=[IO.Path]::GetFileName($buildLog)}
 Write-Output "BUILD $projectName exit=$buildExit"
 if ($buildExit -ne 0) { Get-Content -LiteralPath $buildLog -Tail 20; throw 'Build failed' }
}
$appDll = (Resolve-Path tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll).Path
foreach ($flag in @('--manual-supply','--rcs-timeout-errors','--rcs-heartbeat','--camera-rounds','--shutdown-resources','--ux-commercial-presentation','--mvs-software-flow')) {
 $logName = 'application-' + $flag.Substring(2) + '.log'; $suiteLog = Join-Path $taskEvidence $logName
 & dotnet $appDll . $flag 2>&1 | Out-File -LiteralPath $suiteLog -Encoding utf8
 $suiteExit = $LASTEXITCODE; $passCount = @(Select-String -LiteralPath $suiteLog -Pattern '^PASS ').Count
 $suiteRows += @{flag=$flag;kind='Application';exit_code=$suiteExit;pass_count=$passCount;log=$logName}
 Write-Output "APPLICATION $flag exit=$suiteExit pass=$passCount"
 if ($suiteExit -ne 0) { Get-Content -LiteralPath $suiteLog -Tail 20; throw 'Application test failed' }
}
$uiDll = (Resolve-Path tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll).Path
foreach ($flag in @('--operator-ux-18','--operator-workspace','--manual-supply-ui','--workstation-barcode-ready','--workstation-status-logs','--query-filters','--parameter-v3-only','--station-responsive-ui','--station-latency-ui','--presentation-ordering-ui','--camera-stream-ui','--camera-preview-lifecycle','--camera-round-ui','--continuous-pda-input')) {
 $before = @(Get-ChildItem $env:TEMP -Directory -Filter 'ipc-sportmaster-wpf-*' | ForEach-Object FullName)
 & dotnet $uiDll . $flag
 $suiteExit = $LASTEXITCODE
 $created = @(Get-ChildItem $env:TEMP -Directory -Filter 'ipc-sportmaster-wpf-*' | Where-Object FullName -NotIn $before)
 if ($created.Count -ne 1) { throw 'Expected exactly one native evidence folder' }
 $rawLog = Join-Path $created[0].FullName 'WINDOWS-WPF.log'; $logName = 'native-' + $flag.Substring(2) + '.log'
 Copy-Item -LiteralPath $rawLog -Destination (Join-Path $taskEvidence $logName)
 $passCount = @(Select-String -LiteralPath $rawLog -Pattern '^PASS ').Count
 $suiteRows += @{flag=$flag;kind='WindowsNativeWpf';exit_code=$suiteExit;pass_count=$passCount;log=$logName;native_folder=$created[0].FullName}
 Write-Output "NATIVE $flag exit=$suiteExit pass=$passCount folder=$($created[0].FullName)"
 if ($suiteExit -ne 0) { Get-Content -LiteralPath $rawLog -Tail 15; $suiteRows | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $taskEvidence 'partial-suites.json'); throw 'Native verification failed; preserve guard and inspect' }
}
$report = @{source_commit=$sourceCommit;baseline_main='3b7779d9844337b827c95b7f755b90256ee9d190';builds=$buildRows;suites=$suiteRows;scope='SM-OPERATOR-UX-18; all raw logs retain this source. Synthetic Windows WPF/SQLite/built-in RCS loopback, physical PDA/MVS/server/customer RCS/gloves/sound audibility/site NOT_RUN';assertion_total=($suiteRows | Measure-Object -Property pass_count -Sum).Sum}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskEvidence 'summary.json') -Encoding utf8
Write-Output "FINAL_EVIDENCE=$taskEvidence"
Write-Output "PASS_TOTAL=$($report.assertion_total)"

