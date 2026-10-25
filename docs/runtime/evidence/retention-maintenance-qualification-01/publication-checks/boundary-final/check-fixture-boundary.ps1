$ErrorActionPreference='Stop'
$workspace='D:/projects/importer-pc-retention-verification'
. (Join-Path $workspace 'scripts/project-environment.ps1')
$PSNativeCommandUseErrorActionPreference=$false
Add-Type 'using System.Runtime.InteropServices; public static class QualificationBoundaryErrorMode { [DllImport("kernel32.dll")] public static extern uint GetErrorMode(); [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint value); }'
[QualificationBoundaryErrorMode]::SetErrorMode([QualificationBoundaryErrorMode]::GetErrorMode() -bor 3) | Out-Null
$proof='D:/projects/ipc-retention-qualification-publication-20261025/fixture-boundary-final'
$fixture=Join-Path $workspace 'tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll'
$seed='D:/projects/ipc-retention-full-scale-corrected-20261024/published-seed'
$badSeed=Join-Path $proof 'wrong-station-seed'
New-Item -ItemType Directory -Path $badSeed | Out-Null
Copy-Item -LiteralPath (Join-Path $seed 'configuration.json') -Destination $badSeed
$operation=Get-Content -LiteralPath (Join-Path $seed 'operation.json') -Raw | ConvertFrom-Json -Depth 100
$operation.FrozenConfiguration.StationId='../unrelated-station'
$operation|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $badSeed 'operation.json') -Encoding utf8NoBOM
$badCase=Join-Path $proof 'must-refuse-wrong-station'
& dotnet $fixture $workspace --retention-qualification create $badCase $badSeed 1000 *> (Join-Path $proof 'wrong-station.out.log')
$badExit=$LASTEXITCODE
if($badExit -eq 0 -or @(Get-ChildItem -LiteralPath $badCase -Force).Count -ne 0){throw 'Unrelated station was accepted or wrote a database'}
& dotnet $fixture $workspace --retention-qualification create (Join-Path $proof 'positive-1000') $seed 1000 *> (Join-Path $proof 'positive-1000.out.log')
if($LASTEXITCODE -ne 0){throw 'Valid complete reference fixture was refused'}
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $workspace 'scripts/verify-retention-maintenance.ps1'),[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Published helper syntax failed'}
$loop=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.ForStatementAst] -and $node.Extent.Text.Contains('Linked qualification ancestor refused')},$true))
if($loop.Count -ne 1){throw 'Expected the actual published ancestor guard'}
$guard=[ScriptBlock]::Create($loop[0].Extent.Text)
$ordinary=Join-Path $proof 'ordinary[synthetic]';New-Item -ItemType Directory -Path $ordinary | Out-Null
$protectedRoot=Join-Path $ordinary 'Qualification/new-case';. $guard
$junction=Join-Path $proof 'linked-qualification'
New-Item -ItemType Junction -Path $junction -Target $ordinary | Out-Null
$protectedRoot=Join-Path $junction 'new-case';$denied=$false
try{. $guard}catch{if($_.Exception.Message -notmatch 'Linked qualification ancestor refused'){throw};$denied=$true}
if(-not $denied -or (Test-Path -LiteralPath (Join-Path $ordinary 'new-case'))){throw 'Junction ancestor was accepted or guard created output'}
$receipt=[ordered]@{Status='PASS_FIXTURE_ENTRY_BOUNDARIES_ONLY';AtUtc=[DateTimeOffset]::UtcNow.ToString('o');ParentHead=(& git -C $workspace rev-parse HEAD).Trim();FixtureSourceSha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'tests/Ipc.Application.Verification/RetentionQualification.cs')).Hash;HelperSourceSha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'scripts/verify-retention-maintenance.ps1')).Hash;FixtureDllSha256=(Get-FileHash -LiteralPath $fixture).Hash;WrongStationExitCode=$badExit;WrongStationDatabaseCreated=$false;ValidCompleteFixtureCount=1000;LiteralOrdinaryParentAccepted=$true;ActualPublishedLoopJunctionRefused=$denied;ElevatedOrProtectedCliExecuted=$false;Scope='Ordinary-token fixture entry checks only. No new maintenance/RTO/recovery or target/24h/72h/field qualification. All originals retained.'}
$receipt|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $proof 'RECEIPT.json') -Encoding utf8NoBOM
$receipt|ConvertTo-Json -Depth 5
