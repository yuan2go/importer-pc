#requires -Version 7.0
param([switch]$Fix)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$files = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'industrial-format-files.txt') | Where-Object { $_.Trim().Length -gt 0 })
if ($files.Count -eq 0 -or @($files | Sort-Object -Unique).Count -ne $files.Count) { throw 'Format scope must be non-empty and unique' }
foreach ($file in $files) {
    if ($file -notmatch '^(src|tests)/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\.cs$' -or
        -not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Format scope missing or unsafe: $file" }
}
# Folder mode does not load/build/restore a project. The SDK rejects combining
# --folder with --no-restore; locked restore remains in the separate build job.
$arguments = @('format', 'whitespace', $root, '--folder', '--include') + $files
if (-not $Fix) { $arguments += '--verify-no-changes' }
Push-Location $root
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Incremental format check failed ($LASTEXITCODE)" }
    Write-Output "PASS FORMAT: $($files.Count) explicit IE01 files; remaining repository formatting is outside this check"
} finally { Pop-Location }
