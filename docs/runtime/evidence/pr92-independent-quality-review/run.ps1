param([string]$Name,[string]$Program,[string[]]$Arguments)
. D:/projects/importer-pc-aqa01/scripts/project-environment.ps1
$taskEvidence='D:/audits/importer-pc/AQA01-pr92-independent'
New-Item -ItemType Directory -Force -Path ($taskEvidence+'/logs'),($taskEvidence+'/commands') | Out-Null
$taskStart=[DateTimeOffset]::UtcNow
$taskBase=git rev-parse HEAD
$taskTestFiles=Get-ChildItem tests/Ipc.Application.Verification/ArchitectureQualityRemediation*.cs | ForEach-Object { [pscustomobject]@{Path=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} }
$taskLog=Join-Path $taskEvidence ('logs/'+$Name+'.log')
& $Program @Arguments 2>&1 | Out-File -LiteralPath $taskLog -Encoding utf8
$taskExit=$LASTEXITCODE
[pscustomobject]@{Name=$Name;Program=$Program;Arguments=$Arguments;WorkingDirectory=(Get-Location).Path;StartedAt=$taskStart.ToString('o');FinishedAt=[DateTimeOffset]::UtcNow.ToString('o');BaseCommit=$taskBase;TestFiles=$taskTestFiles;ExitCode=$taskExit;Log=$taskLog} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskEvidence ('commands/'+$Name+'.json'))
Get-Content -LiteralPath $taskLog -Tail 18
exit $taskExit
