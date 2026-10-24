#requires -Version 7.0
param(
    [ValidateSet('Verify','Diagnose','Install','ProvisionData','Run','Console','Shortcut','Inspect','Backup','PlanRestore','Restore','Export')][string]$Action = 'Run',
    [string]$Release = $PSScriptRoot,
    [string]$DataRoot,
    [string]$Configuration,
    [string]$Database,
    [string]$File,
    [string]$Destination,
    [string]$RuntimeAccount,
    [ValidateSet('classic','supply-station')][string]$Ui = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Environment]::OSVersion.Version.Major -ne 10 -or [Environment]::OSVersion.Version.Build -lt 14393 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64' -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Functional baseline matches GUI: Windows 10.0 build 14393+ with native x64 OS/process / PowerShell 7. Device and 24x7 qualification are separate.'
}
function Ordinary([string]$Path) {
    $pathInfo = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path))
    while ($null -ne $pathInfo) {
        if ((Test-Path -LiteralPath $pathInfo.FullName) -and ((Get-Item -LiteralPath $pathInfo.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked path refused: $($pathInfo.FullName)" }
        $pathInfo = $pathInfo.Parent
    }
}
$Release = (Resolve-Path -LiteralPath $Release).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
Ordinary $Release
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content -LiteralPath (Join-Path $Release 'SHA256SUMS')) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'Invalid release checksum entry' }
    $hash = $Matches[1]; $relative = $Matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $Release $relative))
    if (-not $path.StartsWith($Release + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($path)) { throw 'Unsafe or duplicate release entry' }
    Ordinary $path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { throw "Release file changed: $relative" }
}
foreach ($required in @('ASSEMBLY.json','app/Ipc.Assembly.exe','app/application-release.json','desktop/Ipc.Desktop.exe','desktop/application-release.json','desktop/config/business.json','application.ps1')) {
    if (-not $seen.Contains([IO.Path]::GetFullPath((Join-Path $Release $required)))) { throw "Required Application file missing from checksums: $required" }
}
foreach ($item in Get-ChildItem -LiteralPath $Release -Recurse -Force) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked release entry refused' }
    if (-not $item.PSIsContainer -and $item.Name -ne 'SHA256SUMS' -and $item.FullName -ne (Join-Path $Release 'deployment-location.json') -and -not $seen.Contains($item.FullName)) { throw "Unlisted release file: $($item.FullName)" }
}
$assembly = Get-Content -LiteralPath (Join-Path $Release 'ASSEMBLY.json') -Raw | ConvertFrom-Json
$app = Join-Path $Release 'app/Ipc.Assembly.exe'
function Invoke-App([string[]]$Arguments) {
    & $app @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Application maintenance failed ($LASTEXITCODE). Original data, release and incomplete output are retained." }
}
$installed = Invoke-App @('--action','installed-location') | ConvertFrom-Json
if ($Action -eq 'Verify') { [ordered]@{Status='FilesVerified';Application=$assembly.ApplicationId;SourceCommit=$assembly.SourceCommit;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $Release 'SHA256SUMS')).Hash;Windows='NOT_RUN';Production='NOT_QUALIFIED'} | ConvertTo-Json; return }
if (-not $Ui) { $Ui = if ($assembly.ApplicationId -eq 'sportmaster') { 'supply-station' } else { 'classic' } }
if ($Ui -eq 'supply-station' -and $assembly.ApplicationId -ne 'sportmaster') { throw 'supply-station belongs only to Sportmaster' }
if (-not $DataRoot -and $Action -in @('Run','Console','Shortcut')) {
    $DataRoot = if ($null -ne $installed) { $installed.DataRoot } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ImporterPc/data' }
}
if ($DataRoot) {
    $DataRoot = [IO.Path]::GetFullPath($DataRoot); Ordinary $DataRoot
    if ($DataRoot.Equals($Release,[StringComparison]::OrdinalIgnoreCase) -or $DataRoot.StartsWith($Release + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime data must be outside immutable release' }
}
if (-not $Configuration) { $Configuration = if ($null -ne $installed) { $installed.ConfigurationDirectory } else { Join-Path $Release 'desktop/config' } }
$Configuration = (Resolve-Path -LiteralPath $Configuration).Path; Ordinary $Configuration
# Runtime entry points install missing machine policy through the same UAC helper
# as direct EXE startup, then revalidate it. Diagnose remains strictly read-only.
if ($Action -eq 'Console') { Invoke-App @('--action','runtime-prepare') }
if ($Action -eq 'Diagnose') { Invoke-App @('--action','runtime-check') }
switch ($Action) {
    Install {
        $setupArgs = @('--setup','--config',$Configuration)
        if ($DataRoot) { $setupArgs += @('--data-root',$DataRoot) }
        & (Join-Path $Release 'desktop/Ipc.Desktop.exe') @setupArgs
        if ($LASTEXITCODE -ne 0) { throw "Installation wizard failed ($LASTEXITCODE); existing data retained" }
        return
    }
    Diagnose {
        Invoke-App @('--action','business-identity')
        Invoke-App @('--action','deployment-check','--file',(Join-Path $Configuration 'business.json'))
        $policy = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ImporterPc/runtime-policy.json'
        [ordered]@{Status='DependenciesInspected';RuntimeAccountMatches=$true;MachinePolicy=$policy;DataRoot=$DataRoot;Windows='NOT_RUN';SdkAndPhysicalReadiness='NOT_VERIFIED'} | ConvertTo-Json
    }
    ProvisionData {
        if (-not $DataRoot -or -not $RuntimeAccount) { throw 'DataRoot and RuntimeAccount are required' }
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        try { if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'ProvisionData requires elevated maintenance' } } finally { $identity.Dispose() }
        if (Test-Path -LiteralPath $DataRoot) { throw 'Data root already exists; retained for ACL inspection, never reset or replaced' }
        $sid = [Security.Principal.NTAccount]::new($RuntimeAccount).Translate([Security.Principal.SecurityIdentifier])
        $acl = [Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false)
        $admin = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'); $acl.SetOwner($admin)
        $inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
        foreach ($accountSid in @($admin,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'),$sid)) {
            $rights = if ($accountSid.Value -eq $sid.Value) { [Security.AccessControl.FileSystemRights]::Modify } else { [Security.AccessControl.FileSystemRights]::FullControl }
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($accountSid,$rights,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
        }
        New-Item -ItemType Directory -Path $DataRoot | Out-Null; Set-Acl -LiteralPath $DataRoot -AclObject $acl
        [ordered]@{Status='DataRootProvisioned';DataRoot=$DataRoot;RuntimeSid=$sid.Value;ActualOrdinaryAccountRun='NOT_RUN'} | ConvertTo-Json
    }
    Run {
        if (-not $DataRoot) { throw 'Explicit DataRoot required' }
        & (Join-Path $Release 'desktop/Ipc.Desktop.exe') --config $Configuration --data-root $DataRoot --ui $Ui
        if ($LASTEXITCODE -ne 0) { throw "Desktop failed ($LASTEXITCODE); retain original root and diagnose. No fallback root was created." }
    }
    Console { if (-not $DataRoot) { throw 'Explicit DataRoot required' }; Invoke-App @('--config',$Configuration,'--data-root',$DataRoot) }
    Shortcut {
        if (-not $DataRoot -or -not $File -or [IO.Path]::GetExtension($File) -ne '.lnk') { throw 'Explicit DataRoot and a new .lnk File are required' }
        $File = [IO.Path]::GetFullPath($File); Ordinary $File
        if (Test-Path -LiteralPath $File) { throw 'Existing shortcut retained; choose a new explicit filename' }
        $shell = New-Object -ComObject WScript.Shell
        try {
            $link = $shell.CreateShortcut($File); $link.TargetPath = (Get-Process -Id $PID).Path
            $link.Arguments = '-NoProfile -File "' + (Join-Path $Release 'application.ps1') + '" -Action Run -DataRoot "' + $DataRoot + '" -Configuration "' + $Configuration + '" -Ui ' + $Ui
            $link.WorkingDirectory = $Release; $link.Description = $assembly.ApplicationId + ' / ' + $assembly.SourceCommit; $link.Save()
        } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
        Get-Item -LiteralPath $File
    }
    Inspect { if (-not $Database) { throw 'Original Database required; stop runtime first' }; Invoke-App @('--action','maintenance-inspect','--file',([IO.Path]::GetFullPath($Database))) }
    Backup { if (-not $Database -or -not $Destination) { throw 'Original Database and new Destination required; stop runtime first' }; Invoke-App @('--action','backup-offline','--file',([IO.Path]::GetFullPath($Database)),'--destination',([IO.Path]::GetFullPath($Destination))) }
    PlanRestore { if (-not $File -or -not $Destination) { throw 'Backup File and new full Application directory Destination required' }; Invoke-App @('--action','recovery-plan','--file',([IO.Path]::GetFullPath($File)),'--destination',([IO.Path]::GetFullPath($Destination))) }
    Restore { if (-not $File -or -not $Destination) { throw 'Backup File and new full Application directory Destination required' }; Invoke-App @('--action','restore','--file',([IO.Path]::GetFullPath($File)),'--destination',([IO.Path]::GetFullPath($Destination))) }
    Export { if (-not $Database -or -not $Destination) { throw 'Original Database and new diagnostic ZIP Destination required' }; Invoke-App @('--action','diagnostic-export','--file',([IO.Path]::GetFullPath($Database)),'--destination',([IO.Path]::GetFullPath($Destination))) }
}
