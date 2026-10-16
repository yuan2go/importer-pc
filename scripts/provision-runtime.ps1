param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RuntimeAccount
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Machine ownership provisioning requires Windows PowerShell 7 on Windows' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this installation-only script in an elevated maintenance session'
    }
} finally { $identity.Dispose() }
try {
    $runtimeSid = [Security.Principal.NTAccount]::new($RuntimeAccount).Translate([Security.Principal.SecurityIdentifier])
} catch { throw "The explicitly selected runtime account could not be resolved: $RuntimeAccount" }
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$machineRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'ImporterPc'
$leaseDirectory = Join-Path $machineRoot 'leases'
$policyPath = Join-Path $machineRoot 'runtime-policy.json'
function Assert-OrdinaryDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Provisioning refuses a file/reparse point at the machine ownership directory: $Path"
        }
    }
}
Assert-OrdinaryDirectory $machineRoot
Assert-OrdinaryDirectory $leaseDirectory
if (Test-Path -LiteralPath $policyPath) { throw 'Existing machine policy is retained; changing accounts/policy requires a separate reviewed maintenance procedure' }
if ((Test-Path -LiteralPath $leaseDirectory) -and @(Get-ChildItem -LiteralPath $leaseDirectory -Force).Count -ne 0) {
    throw 'Lease directory is not empty; do not remove lease files or provision over an existing station deployment'
}
if ((Test-Path -LiteralPath $machineRoot) -and @(Get-ChildItem -LiteralPath $machineRoot -Force | Where-Object { $_.FullName -ne $leaseDirectory }).Count -ne 0) {
    throw 'Machine directory contains other material; it is retained and requires explicit maintenance review'
}
$inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
$propagate = [Security.AccessControl.PropagationFlags]::None
$allow = [Security.AccessControl.AccessControlType]::Allow
function New-DirectoryPolicy([Security.AccessControl.FileSystemRights]$RuntimeRights) {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administratorsSid)
    foreach ($sid in @($systemSid, $administratorsSid)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, [Security.AccessControl.FileSystemRights]::FullControl, $inherit, $propagate, $allow))
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($runtimeSid, $RuntimeRights, $inherit, $propagate, $allow))
    return $acl
}
# Do not inherit ProgramData's broad writer rules. The parent/policy are read-only to
# the single runtime account; only the child lease directory permits its lock-file writes.
if (-not (Test-Path -LiteralPath $machineRoot)) { New-Item -ItemType Directory -Path $machineRoot | Out-Null }
Set-Acl -LiteralPath $machineRoot -AclObject (New-DirectoryPolicy ([Security.AccessControl.FileSystemRights]::ReadAndExecute))
if (-not (Test-Path -LiteralPath $leaseDirectory)) { New-Item -ItemType Directory -Path $leaseDirectory | Out-Null }
Set-Acl -LiteralPath $leaseDirectory -AclObject (New-DirectoryPolicy ([Security.AccessControl.FileSystemRights]::Modify))
# Write last. A partial failure leaves its files/ACLs for inspection; this script never
# deletes an existing policy or lock and never starts a runtime, SDK, fixture or device.
$policy = [ordered]@{ SchemaVersion = 1; RuntimeAccountSid = $runtimeSid.Value; LeaseRoot = $leaseDirectory }
$json = $policy | ConvertTo-Json
$policyStream = [IO.FileStream]::new($policyPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $policyStream.Write($bytes, 0, $bytes.Length)
    $policyStream.Flush($true)
} finally { $policyStream.Dispose() }
$fileAcl = [Security.AccessControl.FileSecurity]::new()
$fileAcl.SetAccessRuleProtection($true, $false)
$fileAcl.SetOwner($administratorsSid)
foreach ($sid in @($systemSid, $administratorsSid)) {
    $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, [Security.AccessControl.FileSystemRights]::FullControl, $allow))
}
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($runtimeSid, [Security.AccessControl.FileSystemRights]::Read, $allow))
Set-Acl -LiteralPath $policyPath -AclObject $fileAcl
[ordered]@{ Status = 'MachinePolicyProvisioned'; Policy = $policyPath; LeaseRoot = $leaseDirectory;
    RuntimeAccount = $RuntimeAccount; SupportedAccountScope = 'One designated account across login sessions';
    RuntimeValidation = 'NOT_RUN'; WindowsValidation = 'NOT_RUN'; DeviceControlOwnership = 'NOT_ESTABLISHED' } | ConvertTo-Json
