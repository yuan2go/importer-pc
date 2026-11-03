#requires -Version 7.0
# Whitespace/layout gate for every C# file under src and tests (.editorconfig rules).
# -Fix rewrites the files; without it any difference fails the check.
param([switch]$Fix)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
# Folder mode does not load/build/restore a project. The SDK rejects combining
# --folder with --no-restore; locked restore remains in the separate build job.
$arguments = @('format', 'whitespace', $root, '--folder', '--include', 'src', 'tests')
if (-not $Fix) { $arguments += '--verify-no-changes' }
Push-Location $root
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Format check failed ($LASTEXITCODE); run ./scripts/verify-industrial-format.ps1 -Fix and commit the result separately" }
    $count = @(Get-ChildItem -LiteralPath (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count
    Write-Output "PASS FORMAT: $count C# files under src and tests"
} finally { Pop-Location }
