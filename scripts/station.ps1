param(
    [Parameter(Mandatory)][ValidateSet('Run','Fixture','Init','Backup','Preflight','Restore','Export')][string]$Action,
    [string]$Release = $PSScriptRoot,
    [string]$DataRoot = "$env:LOCALAPPDATA/ImporterPc/data",
    [string]$File,
    [ValidateSet('workstation','history','alarms','diagnostics','maintenance','engineering')][string]$View,
    [ValidateSet('standard','compact')][string]$Layout
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 26100 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Reference deployment requires Windows build 26100+ x64; this host has not met that deployment baseline'
}
$Release = (Resolve-Path $Release).Path
$app = Join-Path $Release 'app/Ipc.Product.exe'
$config = Join-Path $Release 'app/config'
function Invoke-Product { & $app --config $config --data-root $DataRoot @args; if ($LASTEXITCODE -ne 0) { throw "Maintenance failed ($LASTEXITCODE); inspect original data, retained backup and operation receipt before retry" } }
function Verify-Release([string]$Directory) {
    $base = (Resolve-Path $Directory).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $entries = @(Get-Content (Join-Path $base 'SHA256SUMS'))
    if ($entries.Count -eq 0) { throw 'Empty checksum list' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in $entries) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid checksum list' }
        $hash = $Matches[1]; $relative = $Matches[2]
        $path = [IO.Path]::GetFullPath((Join-Path $base $relative))
        if (-not $path.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($path)) { throw 'Unsafe or duplicate release path' }
        if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $hash) { throw "Changed release file: $relative" }
    }
    foreach ($required in @('app/Ipc.Product.exe','app/Ipc.Product.dll','app/Ipc.Desktop.exe','app/config/product.json','app/config/station.json','app/config/presentation.json')) {
        if (-not $seen.Contains([IO.Path]::GetFullPath((Join-Path $base $required)))) { throw "Release checksum list missing required file: $required" }
    }
}
# Every action resolves the explicitly selected current release.
switch ($Action) {
    Run {
        Verify-Release $Release
        $presentation = @()
        if ($View) { $presentation += @('--view', $View) }
        if ($Layout) { $presentation += @('--layout', $Layout) }
        Write-Output "Starting explicitly selected release: $Release"
        & (Join-Path $Release 'app/Ipc.Desktop.exe') --config $config --data-root $DataRoot @presentation
        if ($LASTEXITCODE -ne 0) { throw "Selected desktop exited with failure ($LASTEXITCODE); program, configuration, data and backup retained" }
    }
    Fixture {
        Verify-Release $Release
        $fixture = Join-Path $Release 'fixture/Ipc.Fixture.exe'
        if (-not (Test-Path $fixture)) { throw 'This release has no simulation fixture' }
        & $fixture --url 'http://127.0.0.1:18743/' --data "$env:LOCALAPPDATA/ImporterPc/SimulationFixture"
    }
    Init { Verify-Release $Release; Invoke-Product --action snapshot }
    Backup { if (-not $File) { throw 'New backup filename required' }; Verify-Release $Release; Invoke-Product --action backup --file $File }
    Preflight { Verify-Release $Release; Invoke-Product --action preflight }
    Restore { if (-not $File) { throw 'Backup required; select a NEW DataRoot' }; Verify-Release $Release; Invoke-Product --action restore --file $File }
    Export { if (-not $File) { throw 'New diagnostic filename required' }; Verify-Release $Release; Invoke-Product --action export --file $File }
}
