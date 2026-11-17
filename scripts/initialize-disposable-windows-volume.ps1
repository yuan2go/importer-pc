$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Only the disposable GitHub-hosted Windows runner may prepare this volume fixture.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Volume fixture preparation requires an elevated runner token.'
    }
} finally { $identity.Dispose() }

# Production refuses a protected child on a volume whose root permits its
# replacement. Prepare that prerequisite only on the disposable runner; retain
# its other permissions so checkout/build paths remain usable.
$volume = Get-Item -LiteralPath 'D:/'
if (-not $volume.PSIsContainer -or ($volume.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected an ordinary D volume root.' }
$acl = Get-Acl -LiteralPath $volume.FullName
$acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
$replacement = [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
    if ($rule.AccessControlType -ne 'Allow' -or $rule.IdentityReference.Value -in 'S-1-5-18','S-1-5-32-544','S-1-3-0' -or
        ($rule.FileSystemRights -band $replacement) -eq 0) { continue }
    $acl.RemoveAccessRuleSpecific($rule)
    $remaining = $rule.FileSystemRights -band (-bnot $replacement)
    if ($remaining -ne 0) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($rule.IdentityReference, $remaining,
            $rule.InheritanceFlags, $rule.PropagationFlags, $rule.AccessControlType))
    }
}
Set-Acl -LiteralPath $volume.FullName -AclObject $acl
$actual = Get-Acl -LiteralPath $volume.FullName
if ($actual.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne 'S-1-5-32-544' -or
    @($actual.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object {
        $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -notin 'S-1-5-18','S-1-5-32-544','S-1-3-0' -and
        ($_.FileSystemRights -band $replacement) -ne 0
    }).Count -ne 0) { throw 'Disposable volume replacement protection was not established.' }
Write-Output 'PASS disposable D volume root protects administrator-owned children; remaining ACLs retained.'
