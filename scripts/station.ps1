param(
    [Parameter(Mandatory)][ValidateSet('Run','Fixture','Init','Backup','Preflight','Restore','Upgrade','Rollback','Export')][string]$Action,
    [string]$Release = $PSScriptRoot,
    [string]$DataRoot = "$env:LOCALAPPDATA/ImporterPc/data",
    [string]$File,
    [string]$NextRelease,
    [string]$Receipt,
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
function Verify-RollbackIdentity([string]$Directory, $Identity) {
    if ((Get-FileHash (Join-Path $Directory 'app/Ipc.Product.exe') -Algorithm SHA256).Hash -ne $Identity.ExecutableSha256 -or
        (Get-FileHash (Join-Path $Directory 'app/Ipc.Product.dll') -Algorithm SHA256).Hash -ne $Identity.ProgramAssemblySha256 -or
        (Get-FileHash (Join-Path $Directory 'SHA256SUMS') -Algorithm SHA256).Hash -ne $Identity.ReleaseManifestSha256) { throw 'Rollback release differs from the recorded previous program/release identity' }
    foreach ($name in @('product.json','station.json','presentation.json')) {
        if ((Get-FileHash (Join-Path $Directory "app/config/$name") -Algorithm SHA256).Hash -ne $Identity.ConfigurationSha256.$name) { throw "Rollback configuration differs from recorded previous configuration: $name" }
    }
}
# One selection protocol: every action resolves the explicit Release (or the directory
# containing this script). Upgrade/Rollback never create an unconsumed active-release pointer.
switch ($Action) {
    Run {
        Verify-Release $Release
        $presentation = @()
        if ($View) { $presentation += @('--view', $View) }
        if ($Layout) { $presentation += @('--layout', $Layout) }
        Write-Output "Starting explicitly selected release: $Release"
        & (Join-Path $Release 'app/Ipc.Desktop.exe') --config $config --data-root $DataRoot @presentation
        if ($LASTEXITCODE -ne 0) { throw "Selected desktop exited with failure ($LASTEXITCODE); program/configuration/data and pre-upgrade backup retained" }
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
    Upgrade {
        if (-not $NextRelease -or -not $File) { throw 'Previous Release, selected NextRelease and NEW backup File required' }
        Verify-Release $Release; Verify-Release $NextRelease
        $previous = $Release; $selected = (Resolve-Path $NextRelease).Path
        if (-not $Receipt) { $Receipt = "$File.upgrade.json" }
        $app = Join-Path $selected 'app/Ipc.Product.exe'; $config = Join-Path $selected 'app/config'
        # The NEW reader inspects/copies the old format read-only. It obtains the same data
        # lease for the complete check -> backup -> durable receipt -> explicit migration.
        Invoke-Product --action upgrade --file $File --receipt $Receipt --previous-release $previous --target-release $selected
        Write-Output "Upgrade maintenance completed; launch NOT_RUN. Selected Release: $selected; DataRoot: $DataRoot; receipt: $Receipt"
        Write-Output 'Run must explicitly use this selected Release and the same DataRoot. Previous files and original-format backup remain retained.'
    }
    Rollback {
        if (-not $NextRelease -or -not $File) { throw 'Recorded previous NextRelease and pre-upgrade backup File required; DataRoot must be NEW' }
        if (-not $Receipt) { $Receipt = "$File.upgrade.json" }
        if (-not (Test-Path $Receipt -PathType Leaf)) { throw 'Original upgrade receipt required to bind backup, previous program and configuration' }
        $operation = Get-Content $Receipt -Raw | ConvertFrom-Json
        if (-not $operation.OperationId -or -not $operation.Backup.BackupId -or $operation.Status -notin @('BackupVerifiedAwaitingUpgrade','CompatibleFormatRetained','ForwardMigrationCommitted','UpgradeFailedInspectBeforeRetry')) { throw 'Unrecognized/incomplete upgrade receipt; explicit recovery review required' }
        if ((Get-FileHash $File -Algorithm SHA256).Hash -ne $operation.Backup.Sha256) { throw 'Backup fingerprint differs from original upgrade receipt' }
        $selected = (Resolve-Path $NextRelease).Path
        Verify-Release $selected; Verify-RollbackIdentity $selected $operation.PreviousRelease
        $app = Join-Path $selected 'app/Ipc.Product.exe'; $config = Join-Path $selected 'app/config'
        $station = Get-Content (Join-Path $config 'station.json') -Raw | ConvertFrom-Json
        $destination = Join-Path (Join-Path $DataRoot $station.Mode) $station.StationId
        # Require a completely fresh station directory, not merely an absent station.db.
        if (Test-Path $destination) { throw 'Rollback requires a new station data root; original data directory retained' }
        Invoke-Product --action restore --file $File
        Write-Output "Restored recorded backup with its matching previous release into quarantine. Source format: $($operation.Backup.Source.FormatVersion); selected Release: $selected; DataRoot: $DataRoot; launch NOT_RUN. Reconcile external ledger/entities before admission."
    }
}
