param(
    [Parameter(Mandatory)][ValidateSet('Simulation','Production')][string]$Mode,
    [Parameter(Mandatory)][string]$TrustFile
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Extension provisioning requires Windows PowerShell 7' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run only this installation step in an elevated maintenance session'
    }
} finally { $identity.Dispose() }
$machineRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'ImporterPc'
$runtimePolicy = Join-Path $machineRoot 'runtime-policy.json'
if (-not (Test-Path -LiteralPath $runtimePolicy -PathType Leaf)) { throw 'Provision fixed ordinary runtime account first with provision-runtime.ps1' }
foreach ($path in @($machineRoot,$runtimePolicy)) {
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked machine policy refused' }
    $security = Get-Acl -LiteralPath $path
    $owner = $security.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($owner -notin @('S-1-5-18','S-1-5-32-544')) { throw 'Machine policy maintenance ownership required' }
}
$policy = Get-Content -LiteralPath $runtimePolicy -Raw | ConvertFrom-Json
$runtimeSid = [Security.Principal.SecurityIdentifier]::new($policy.RuntimeAccountSid)
$trustPath = (Resolve-Path -LiteralPath $TrustFile).Path
if ((Get-Item -LiteralPath $trustPath).Length -gt 524288) { throw 'Offline trust policy size quota' }
$raw = Get-Content -LiteralPath $trustPath -Raw
if ($raw -match 'PRIVATE KEY') { throw 'Private signing keys must never be installed on a client' }
$trust = $raw | ConvertFrom-Json
if ($trust.SchemaVersion -ne 1 -or @($trust.Publishers).Count -eq 0) { throw 'Explicit nonempty independent trust policy required' }
$seen = @{}
foreach ($publisher in $trust.Publishers) {
    if (-not $publisher.Id -or $seen.ContainsKey($publisher.Id) -or $null -eq $publisher.TestOnly -or $publisher.PublicKeyPem -notmatch '^-----BEGIN PUBLIC KEY-----') { throw 'Invalid/duplicate publisher policy' }
    if ($Mode -eq 'Production' -and $publisher.TestOnly) { throw 'Production policy must not contain test publishers' }
    $rsa = [Security.Cryptography.RSA]::Create()
    try { $rsa.ImportFromPem($publisher.PublicKeyPem); if ($rsa.KeySize -lt 3072) { throw 'RSA key must be at least 3072 bits' } } finally { $rsa.Dispose() }
    $seen[$publisher.Id] = $true
}
$target = Join-Path $machineRoot "extension-trust.$Mode.json"
if (Test-Path -LiteralPath $target) { throw 'Existing trust policy retained; rotation requires an explicitly reviewed replacement, old policy and all affected packages preserved' }
$inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
$allow = [Security.AccessControl.AccessControlType]::Allow
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
foreach ($directory in @((Join-Path $machineRoot 'extensions'),(Join-Path $machineRoot "extensions/$Mode"))) {
    if (Test-Path -LiteralPath $directory) {
        $item = Get-Item -LiteralPath $directory -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Extension path is not an ordinary directory' }
        $owner = (Get-Acl -LiteralPath $directory).GetOwner([Security.Principal.SecurityIdentifier]).Value
        if ($owner -notin @('S-1-5-18','S-1-5-32-544')) { throw 'Existing directory has non-maintenance owner; retained for inspection' }
    } else { New-Item -ItemType Directory -Path $directory | Out-Null }
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($administratorsSid)
    foreach ($sid in @($systemSid,$administratorsSid)) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,$allow)) }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($runtimeSid,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,[Security.AccessControl.PropagationFlags]::None,$allow))
    Set-Acl -LiteralPath $directory -AclObject $acl
}
# FIX-14: a protected read-only fence serializes consumer registration with retirement.
# A pre-registration store has unknown offline consumers and cannot be declared empty.
$storeRoot = Join-Path $machineRoot "extensions/$Mode"
$usageRoot = Join-Path $storeRoot 'usage'
if (-not (Test-Path -LiteralPath $usageRoot)) { New-Item -ItemType Directory -Path $usageRoot | Out-Null }
if ((Get-Item -LiteralPath $usageRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked consumer fence directory refused' }
$consumerFence = Join-Path $usageRoot 'consumers.lease'
if (-not (Test-Path -LiteralPath $consumerFence)) {
    $registrations = Join-Path $storeRoot 'registrations'
    $legacy = (Test-Path -LiteralPath $registrations) -and @((Get-ChildItem -LiteralPath $registrations -File -Filter '*.json')).Count -gt 0
    $coverage = if ($legacy) { 'LegacyConsumersUnverified: offline consumer scope predates registration; uninstall refused' } else { 'Extension consumer registration and inspection fence v1' }
    $fenceStream = [IO.FileStream]::new($consumerFence,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($coverage); $fenceStream.Write($bytes); $fenceStream.Flush($true) } finally { $fenceStream.Dispose() }
}
$stream = [IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($raw); $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
# Trust file inherits the already-protected machine parent; enforce its exact file ACL too.
$fileAcl = [Security.AccessControl.FileSecurity]::new(); $fileAcl.SetAccessRuleProtection($true,$false); $fileAcl.SetOwner($administratorsSid)
foreach ($sid in @($systemSid,$administratorsSid)) { $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$allow)) }
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($runtimeSid,[Security.AccessControl.FileSystemRights]::Read,$allow)); Set-Acl -LiteralPath $target -AclObject $fileAcl
[ordered]@{ Status='OfflineExtensionTrustProvisioned'; Mode=$Mode; PolicySha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash; OnlineRevocation='NOT_RUN'; DeviceActions='NOT_RUN'; OrdinaryAccountRuntime='NOT_RUN' } | ConvertTo-Json
