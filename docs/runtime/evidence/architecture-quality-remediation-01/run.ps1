param([string]$Name,[string]$Program,[string[]]$Arguments)
. D:/projects/importer-pc-aqa01/scripts/project-environment.ps1
$taskEvidence='D:/audits/importer-pc/AQA01-remediation-20261023'
$taskStart=[DateTimeOffset]::UtcNow
$taskLog=Join-Path $taskEvidence ('logs/'+$Name+'.log')
& $Program @Arguments 2>&1 | Out-File -LiteralPath $taskLog -Encoding utf8
$taskExit=$LASTEXITCODE
[pscustomobject]@{Name=$Name;Program=$Program;Arguments=$Arguments;WorkingDirectory=(Get-Location).Path;StartedAt=$taskStart.ToString('o');FinishedAt=[DateTimeOffset]::UtcNow.ToString('o');ExitCode=$taskExit;Log=$taskLog} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskEvidence ('commands/'+$Name+'.json'))
Get-Content -LiteralPath $taskLog -Tail 24
exit $taskExit
