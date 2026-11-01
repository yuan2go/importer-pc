param(
    [Parameter(Mandatory)][string]$SourceApp,
    [Parameter(Mandatory)][string]$Output,
    [string]$VerificationExecutable,
    [string]$Dotnet = 'dotnet',
    [switch]$FreshDOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'project-environment.ps1')
$testIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$testPrincipal = New-Object Security.Principal.WindowsPrincipal($testIdentity)
if (-not $testPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run from an existing administrator PowerShell session. No UAC settings or current installation will be changed.'
}
$SourceApp = (Resolve-Path -LiteralPath $SourceApp).Path
$Output = [IO.Path]::GetFullPath($Output)
if (-not $Output.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a new D-drive evidence directory.' }
if (Test-Path -LiteralPath $Output) { throw 'Output must be a new directory; original evidence is never overwritten.' }
if ($VerificationExecutable) {
    $VerificationExecutable = (Resolve-Path -LiteralPath $VerificationExecutable).Path
    $verificationPlatform = Join-Path (Split-Path $VerificationExecutable -Parent) 'Ipc.Platform.dll'
    $entry = $VerificationExecutable
    $prefix = @()
} else {
    $repo = Split-Path $PSScriptRoot -Parent
    $verificationPlatform = Join-Path $repo 'tests\Ipc.Application.Verification\bin\Release\net10.0\Ipc.Platform.dll'
    $entry = $Dotnet
    $prefix = @((Join-Path $repo 'tests\Ipc.Application.Verification\bin\Release\net10.0\Ipc.Application.Verification.dll'), $repo)
}
if ((Get-FileHash -LiteralPath $verificationPlatform).Hash -ne (Get-FileHash -LiteralPath (Join-Path $SourceApp 'Ipc.Platform.dll')).Hash) {
    throw 'Verification and service host must use byte-identical Platform DLLs. Rebuild both from the same source.'
}
$log = $Output + '.log'
if (Test-Path -LiteralPath $log) { throw 'A previous evidence log exists; choose another output.' }
[string[]]$scopeArguments = if ($FreshDOnly) { @('--fresh-d-only') } else { @() }
& $entry @prefix --installer-storage-matrix-direct $SourceApp $Output @scopeArguments *> $log
$matrixExit = $LASTEXITCODE
Get-Content -LiteralPath $log -Tail 25
if ($matrixExit -ne 0) { throw "Matrix failed (exit $matrixExit). Evidence retained: $log" }
if (-not (Test-Path -LiteralPath (Join-Path $Output 'RESULT.json'))) { throw 'No final matrix receipt; do not report PASS.' }
Write-Host ('Completed: ' + $Output)
