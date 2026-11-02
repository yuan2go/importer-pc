param()
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference='Stop'
. ./scripts/project-environment.ps1
$taskSource=(& git rev-parse HEAD).Trim()
if ((& git status --porcelain -- src tests scripts)) { throw 'Commit product/test/tool sources before frozen verification' }
& ./scripts/verify-sm-product-closure-21.ps1
if($LASTEXITCODE -ne 0) { throw 'Inherited product verification failed; raw failures retained' }
$taskRoot=Join-Path (Get-Location) ('.artifacts/db11-compatible-update-22/'+$taskSource)
New-Item -ItemType Directory -Path $taskRoot -Force|Out-Null
$taskResults=[System.Collections.Generic.List[object]]::new()
foreach($taskFlag in @('--db11-compatible-update','--storage-baseline','--stability-audit-maintenance','--maintenance-progress-checks','--commercial-productization','--audit03-maintenance','--manual-rcs-debug-lifecycle')) {
 if ((& git status --porcelain -- src tests scripts)) { throw 'Frozen sources changed during verification' }
 $taskName='app'+$taskFlag
 $taskLog=Join-Path $taskRoot ($taskName+'.log')
 & dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . $taskFlag *> $taskLog
 $taskExit=$LASTEXITCODE
 $taskResults.Add(@{name=$taskName;exit=$taskExit;passes=@(Select-String -LiteralPath $taskLog -Pattern '^PASS').Count})
 @{source_commit=$taskSource;scope='DB10-to-11, original custody, cancellation/deployment failure recovery, Windows-native inherited product regression; installation and physical equipment recorded separately';inherited_summary=('.artifacts/sm-product-closure-21/'+$taskSource+'/all-summary.json');results=$taskResults.ToArray()}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskRoot 'summary.json') -Encoding utf8
 Write-Output ($taskName+' exit='+$taskExit)
 if($taskExit -ne 0){Get-Content -LiteralPath $taskLog -Tail 16}
}
python scripts/test_release_documents.py
if($LASTEXITCODE -ne 0){throw 'Shipped guide verification failed'}
python scripts/test_installer_package.py
if($LASTEXITCODE -ne 0){throw 'Installer package verification failed'}
Write-Output ('EVIDENCE='+$taskRoot)
if(@($taskResults|Where-Object exit -ne 0).Count){exit 1}
