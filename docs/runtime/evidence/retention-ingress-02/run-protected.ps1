$ErrorActionPreference='Stop'
$taskWorkspace='D:/w/ipc-retention-ingress-02'
$taskEvidence='D:/projects/ipc-retention-ingress-evidence-20261025'
try {
 & "$taskWorkspace/scripts/verify-retention-maintenance.ps1" -Workspace $taskWorkspace -Evidence $taskEvidence -Seed 'D:/projects/ipc-retention-full-scale-corrected-20261024/published-seed' -Counts @(1000,10000,604800,2592000) -RestoreAll -ReuseCompletedRun 'D:/projects/ipc-retention-full-scale-corrected-20261024/PROTECTED-RESULT.json'
 [ordered]@{Status='CONTROLLER_RETURNED';AtUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$taskEvidence/HELPER-END.json" -Encoding utf8NoBOM
} catch {
 [ordered]@{Status='FAILED';Error=$_.Exception.ToString();AtUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$taskEvidence/HELPER-END.json" -Encoding utf8NoBOM
 exit 1
}
