param(
    [Parameter(Mandatory)][ValidateSet('Run','Fixture','Init','Backup','Preflight','Restore','Upgrade','Rollback','Export')][string]$Action,
    [string]$Release = $PSScriptRoot,
    [string]$DataRoot = "$env:LOCALAPPDATA/ImporterPc/data",
    [string]$File,
    [string]$NextRelease,
    [ValidateSet('workstation','history','alarms','diagnostics','maintenance','engineering')][string]$View,
    [ValidateSet('standard','compact')][string]$Layout
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 26100 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Reference deployment requires Windows build 26100+ x64; this host has not met that deployment baseline'
}
$app = Join-Path $Release 'app/Ipc.Product.exe'
$config = Join-Path $Release 'app/config'
function Invoke-Product { & $app --config $config --data-root $DataRoot @args; if ($LASTEXITCODE -ne 0) { throw "Maintenance failed ($LASTEXITCODE); data retained" } }
function Verify-Release([string]$Directory) {
    $base = (Resolve-Path $Directory).Path
    foreach ($line in Get-Content (Join-Path $base 'SHA256SUMS')) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid checksum list' }
        $hash = $Matches[1]; $relative = $Matches[2]
        $path = [IO.Path]::GetFullPath((Join-Path $base $relative))
        if (-not $path.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release path' }
        if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $hash) { throw "Changed release file: $relative" }
    }
}
switch ($Action) {
    Run {
        Verify-Release $Release
        $presentation = @()
        if ($View) { $presentation += @('--view', $View) }
        if ($Layout) { $presentation += @('--layout', $Layout) }
        & (Join-Path $Release 'app/Ipc.Desktop.exe') --config $config --data-root $DataRoot @presentation
    }
    Fixture {
        $fixture = Join-Path $Release 'fixture/Ipc.Fixture.exe'
        if (-not (Test-Path $fixture)) { throw 'This release has no simulation fixture' }
        & $fixture --url 'http://127.0.0.1:18743/' --data "$env:LOCALAPPDATA/ImporterPc/SimulationFixture"
    }
    Init { Verify-Release $Release; Invoke-Product --action snapshot }
    Backup { if (-not $File) { throw 'New backup filename required' }; Invoke-Product --action backup --file $File }
    Preflight { Verify-Release $Release; Invoke-Product --action preflight }
    Restore { if (-not $File) { throw 'Backup required; select a NEW DataRoot' }; Invoke-Product --action restore --file $File }
    Export { if (-not $File) { throw 'New diagnostic filename required' }; Invoke-Product --action export --file $File }
    Upgrade {
        if (-not $NextRelease -or -not $File) { throw 'NextRelease and consistent backup File required' }
        Verify-Release $Release; Verify-Release $NextRelease
        Invoke-Product --action preflight; Invoke-Product --action backup --file $File
        # Releases remain side by side. Switch a pointer atomically; never overwrite a running binary.
        $pointer = Join-Path (Split-Path $Release -Parent) 'active-release.txt'
        $temporary = "$pointer.new"
        (Resolve-Path $NextRelease).Path | Set-Content $temporary
        Move-Item -Force $temporary $pointer
        Write-Output 'Release pointer updated. Start the selected release after reviewing configuration/schema compatibility.'
    }
    Rollback {
        if (-not $NextRelease -or -not $File) { throw 'Previous release and backup required; DataRoot must be NEW' }
        Verify-Release $NextRelease
        $app = Join-Path $NextRelease 'app/Ipc.Product.exe'; $config = Join-Path $NextRelease 'app/config'
        Invoke-Product --action restore --file $File
        Write-Output 'Restored to NEW data root in quarantine. Compare the external ledger before admission; original data retained.'
    }
}
