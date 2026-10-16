param([Parameter(Mandatory)][string]$Release,[Parameter(Mandatory)][string]$RuntimeAccount)
$ErrorActionPreference='Stop'
if (-not $IsWindows) { throw 'Protect the selected release on Windows only' }
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try { if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Release ACL installation requires an elevated maintenance session' } } finally { $identity.Dispose() }
$root=(Resolve-Path -LiteralPath $Release).Path
$runtimeSid=[Security.Principal.NTAccount]::new($RuntimeAccount).Translate([Security.Principal.SecurityIdentifier])
$admin=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544');$system=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$allow=[Security.AccessControl.AccessControlType]::Allow
$inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
# Walk before mutation, refusing all reparse entries. Never follow a link during elevation.
$queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($root);$items=[Collections.Generic.List[IO.FileSystemInfo]]::new()
while ($queue.Count -gt 0) {
    $directory=$queue.Dequeue();$item=Get-Item -LiteralPath $directory -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked release directory refused' }
    $items.Add($item)
    foreach ($child in Get-ChildItem -LiteralPath $directory -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked release entry refused' }
        if ($child.PSIsContainer) {$queue.Enqueue($child.FullName)} else {$items.Add($child)}
    }
}
foreach ($item in $items) {
    if ($item.PSIsContainer) { $acl=[Security.AccessControl.DirectorySecurity]::new() } else { $acl=[Security.AccessControl.FileSecurity]::new() }
    $acl.SetAccessRuleProtection($true,$false);$acl.SetOwner($admin)
    foreach ($sid in @($admin,$system)) {
        if ($item.PSIsContainer) {$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,$allow)}
        else {$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$allow)}
        $acl.AddAccessRule($rule)
    }
    if ($item.PSIsContainer) {$rule=[Security.AccessControl.FileSystemAccessRule]::new($runtimeSid,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,[Security.AccessControl.PropagationFlags]::None,$allow)}
    else {$rule=[Security.AccessControl.FileSystemAccessRule]::new($runtimeSid,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$allow)}
    $acl.AddAccessRule($rule);Set-Acl -LiteralPath $item.FullName -AclObject $acl
}
[ordered]@{Status='SelectedReleaseAclInstalled';RuntimeAccount=$RuntimeAccount;Entries=$items.Count;PublisherSignature='REFERENCE_OFFLINE_CHANNEL_NOT_PRODUCTION_CODESIGN';RuntimeValidation='NOT_RUN'} | ConvertTo-Json
