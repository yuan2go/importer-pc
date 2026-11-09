$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Run the clean-machine runtime installation verification on the disposable GitHub-hosted Windows runner.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'The quiet installation test requires an elevated Windows runner token.'
    }
} finally { $identity.Dispose() }

$verificationRoot = 'D:/sm-vc-acceptance'
$evidence = Join-Path $verificationRoot 'evidence'
$inputRoot = Join-Path $verificationRoot 'redist'
New-Item -ItemType Directory -Path $evidence,$inputRoot | Out-Null
# Quiet deployment only writes a receipt under administrator-controlled ancestors.
$administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$protected = [Security.AccessControl.DirectorySecurity]::new()
$protected.SetOwner($administrators)
$protected.SetAccessRuleProtection($true, $false)
foreach ($principal in $administrators,$system) {
    $protected.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
}
Set-Acl -LiteralPath $verificationRoot -AclObject $protected
Set-Acl -LiteralPath $evidence -AclObject $protected
$inventory = Get-Content -LiteralPath src/Ipc.Platform/sm-vc-runtime.json -Raw | ConvertFrom-Json
foreach ($package in $inventory.Packages) {
    $file = Join-Path $inputRoot $package.File
    curl.exe --fail --silent --show-error --location $package.Url --output $file
    if ($LASTEXITCODE) { throw "Microsoft package download failed: $($package.File)" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Microsoft Corporation,') {
        throw "Microsoft package signature verification failed: $($package.File) / $($signature.Status)"
    }
}
git branch ci/runtime-install HEAD
if ($LASTEXITCODE) { throw 'Could not identify frozen verification source' }
python scripts/package-application.py --application sportmaster --target win-x64 --output "$verificationRoot/media" --source-branch ci/runtime-install --verification-without-vendor-runtime --vc-redist $inputRoot > "$evidence/build-media.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/build-media.log" -Tail 20; throw 'Verification media build failed' }
python scripts/package-installer.py --media "$verificationRoot/media.zip" --output "$verificationRoot/installer" > "$evidence/build-installer.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/build-installer.log" -Tail 20; throw 'Verification installer build failed' }

# Only remove the matching Windows Installer products in this disposable image.
# Run after building, since the runner's own build tools may use these libraries.
$products = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match '^Microsoft Visual C\+\+ (2008|2010)\b.*x64' -and $_.PSChildName -match '^\{[0-9a-fA-F-]{36}\}$' }
foreach ($product in $products) {
    $result = Start-Process msiexec.exe -ArgumentList @('/x', $product.PSChildName, '/qn', '/norestart', '/l*v', "$evidence/remove-$($product.PSChildName).log") -PassThru -Wait -WindowStyle Hidden
    if ($result.ExitCode -notin 0,1605,3010) { throw "Preinstalled runtime removal failed: $($result.ExitCode)" }
}
$systemRuntime = Join-Path $env:WINDIR 'System32\msvcr100.dll'
$sxsRoot = Join-Path $env:WINDIR 'WinSxS'
function Vc90Files {
    @(Get-ChildItem -LiteralPath $sxsRoot -Directory -Filter 'amd64_microsoft.vc90.crt_*' | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_.FullName 'msvcr90.dll')) -and (Test-Path -LiteralPath (Join-Path $_.FullName 'msvcp90.dll'))
    } | Select-Object -ExpandProperty Name)
}
$before = [ordered]@{Vc2010 = Test-Path -LiteralPath $systemRuntime; Vc90 = @(Vc90Files)}
$before | ConvertTo-Json | Set-Content -LiteralPath "$evidence/before.json"
if ($before.Vc2010) { throw 'Verification requires the x64 VC++ 2010 runtime to be absent before install' }
$expectedVc90 = if ($before.Vc90.Count) { 'AlreadyAvailable' } else { 'Installed' }
Write-Output "PASS baseline: x64 VC++ 2010 is absent. VC++ 2008 assembly: $($before.Vc90 -join ', '). Expected VC++ 2008 result: $expectedVc90."

$installer = (Get-ChildItem -LiteralPath "$verificationRoot/installer" -File -Filter '*.exe' | Select-Object -First 1).FullName
function Install-Verification([string]$name, [string]$vc90, [string]$vc100, [string[]]$extra) {
    $id = [Guid]::NewGuid().ToString('N')
    $installerArguments = @('/quiet', '/norestart', "/operationid=$id", "/dataroot=$verificationRoot/data", "/log=$evidence/$name.jsonl") + $extra
    $process = Start-Process -FilePath $installer -ArgumentList $installerArguments -PassThru -Wait -WindowStyle Hidden
    $receipt = "D:/ImporterPc/Machine/deployments/sportmaster.setup-$id.json"
    if (Test-Path -LiteralPath $receipt) { Copy-Item -LiteralPath $receipt -Destination "$evidence/$name-receipt.json" }
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath "$evidence/$name.jsonl") { Get-Content -LiteralPath "$evidence/$name.jsonl" -Tail 8 }
        throw "$name installer failed with exit code $($process.ExitCode)"
    }
    $proof = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    $vc90Status = ($proof.CameraRuntime | Where-Object Package -EQ 'Visual C++ 2008 SP1 x64').Status
    $vc100Status = ($proof.CameraRuntime | Where-Object Package -EQ 'Visual C++ 2010 SP1 x64').Status
    if ($proof.Status -ne 'Completed' -or $proof.BusinessStarted -or $proof.CameraRuntime.Count -ne 2 -or
        $vc90Status -ne $vc90 -or $vc100Status -ne $vc100) {
        throw "$name reported unexpected results: VC++ 2008=$vc90Status, VC++ 2010=$vc100Status"
    }
    Write-Output "PASS ${name}: VC++ 2008=$vc90Status, VC++ 2010=$vc100Status before installation commit."
}
Install-Verification -name 'install' -vc90 $expectedVc90 -vc100 'Installed' -extra @()
if (!(Test-Path -LiteralPath $systemRuntime) -or @(Vc90Files).Count -eq 0) {
    throw 'Both real x64 runtime families must be present after install'
}
Install-Verification -name 'repair' -vc90 'AlreadyAvailable' -vc100 'AlreadyAvailable' -extra @('/repair')

# Windows can supply VC90 in WinSxS. Keep that system component intact and
# exercise the bundled VC90 installer's quiet invocation separately in that case.
if ($expectedVc90 -eq 'AlreadyAvailable') {
    $package = Join-Path $inputRoot 'vc2008-sp1-x64.exe'
    $process = Start-Process -FilePath $package -ArgumentList @('/q', '/norestart') -PassThru -Wait -WindowStyle Hidden
    $products = @(Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match '^Microsoft Visual C\+\+ 2008\b.*x64' } | Select-Object DisplayName,DisplayVersion)
    @{ExitCode = $process.ExitCode; Products = $products} | ConvertTo-Json | Set-Content -LiteralPath "$evidence/vc2008-standalone.json"
    if ($process.ExitCode -notin 0,3010 -or !$products.Count) { throw "Bundled VC++ 2008 silent installation did not register: $($process.ExitCode)" }
    Write-Output "PASS standalone: bundled VC++ 2008 registered after quiet installation, exit $($process.ExitCode)."
}
