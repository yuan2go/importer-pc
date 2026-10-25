#requires -Version 7.0
$ErrorActionPreference='Stop'
$workspace='D:/projects/importer-pc-retention-verification'
. (Join-Path $workspace 'scripts/project-environment.ps1')
$evidence='D:/projects/ipc-retention-ingress-full-retest-20261025'
if(((& git -C $workspace rev-parse HEAD).Trim()) -ne 'b83998f18c56df2dd965630b25908feedc00a56f'){throw 'Fixed product source moved before actual qualification'}
$restored=Get-Content -LiteralPath (Join-Path $evidence 'baseline-recovery/POST-BUDGET-RECOVERY.json') -Raw|ConvertFrom-Json
if($restored.Status -ne 'PASS_SEPARATE_ISOLATED_RECOVERY_DRILL_ONLY'){throw 'Finish the original isolated recovery before the new scale run'}
# Default coverage: first small case restores and repeats, second compacts,
# full case restores the entire source backup then performs complete maintenance.
# No RestoreAll copy for later cases is requested; its additional capacity/result
# remains separate. No timeout, protection predicate or local reserve is reduced.
& (Join-Path $evidence 'EXECUTED-HELPER.ps1') -Workspace $workspace -Evidence (Join-Path $evidence 'protected') -Seed D:/projects/ipc-retention-full-scale-corrected-20261024/published-seed -ReuseCompletedRun D:/projects/ipc-retention-full-scale-corrected-20261024/PROTECTED-RESULT.json
