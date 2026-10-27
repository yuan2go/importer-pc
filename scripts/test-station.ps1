#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Import only pure policy functions. Never execute station.ps1, read HKLM,
# start a product/fixture, or acquire machine leases in this policy test.
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'station.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($name in @('Test-StationPlatform', 'Resolve-StationDataRoot')) {
    $function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $function) { throw "Missing policy function: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL STATION: $Message" }
    Write-Host "PASS STATION: $Message"
}
foreach ($build in @(19045, 22000, 22631, 26100, 26200)) {
    Check (Test-StationPlatform $true ([version]"10.0.$build") 'X64' 'X64') "Win10 22H2 / Win11 build $build admitted"
}
Check (Test-StationPlatform $true ([version]'10.0.14393') 'X64' 'X64') 'runtime floor matches GUI'
foreach ($case in @(
    @($false, '10.0.19045', 'X64', 'X64'), @($true, '6.3.9600', 'X64', 'X64'),
    @($true, '10.0.10240', 'X64', 'X64'), @($true, '10.0.19045', 'X86', 'X86'),
    @($true, '10.0.22631', 'Arm64', 'X64'), @($true, '10.0.22631', 'X64', 'X86')
)) {
    Check (-not (Test-StationPlatform $case[0] ([version]$case[1]) $case[2] $case[3])) "unsupported platform refused: $case"
}
$sid = 'S-1-5-21-100-200-300-400'
Check ((Resolve-StationDataRoot '' 'D:\Synthetic\ImporterPc' $sid) -eq "D:\Synthetic\ImporterPc\UserData\$sid\data") 'registered D root and current SID determine the default'
Check ((Resolve-StationDataRoot 'D:\Synthetic\Explicit' '' '') -eq 'D:\Synthetic\Explicit') 'explicit data root is honored without a registry default'
foreach ($case in @(@('', $sid), @('relative', $sid), @('\\server\share', $sid), @('D:\', $sid), @('D:\Synthetic\ImporterPc', ''))) {
    $refused = $false
    try { $null = Resolve-StationDataRoot '' $case[0] $case[1] } catch { $refused = $true }
    Check $refused "invalid default refuses without LOCALAPPDATA fallback: $case"
}
Write-Host 'PASS STATION COMPLETE'
