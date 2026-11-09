$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Run the clean-machine runtime installation verification on the disposable GitHub-hosted Windows runner.'
}

$verificationRoot = 'D:/sm-vc-acceptance'
$evidence = Join-Path $verificationRoot 'evidence'
$inputRoot = Join-Path $verificationRoot 'redist'
New-Item -ItemType Directory -Path $evidence,$inputRoot | Out-Null
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
if ($before.Vc2010 -or $before.Vc90.Count) { throw 'Verification requires both x64 runtime families to be absent before install' }
Write-Output 'PASS baseline: both x64 runtime families are absent.'

$installer = (Get-ChildItem -LiteralPath "$verificationRoot/installer" -File -Filter '*.exe' | Select-Object -First 1).FullName
function Install-Verification([string]$name, [string]$expected, [string[]]$extra) {
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
    if ($proof.Status -ne 'Completed' -or $proof.BusinessStarted -or $proof.CameraRuntime.Count -ne 2 -or
        @($proof.CameraRuntime | Where-Object Status -NE $expected).Count -ne 0) {
        throw "$name did not record both runtimes as $expected"
    }
    Write-Output "PASS ${name}: both runtime prerequisites reported $expected before installation commit."
}
Install-Verification -name 'install' -expected 'Installed' -extra @()
if (!(Test-Path -LiteralPath $systemRuntime) -or @(Vc90Files).Count -eq 0) {
    throw 'Both real x64 runtime families must be present after install'
}
Install-Verification -name 'repair' -expected 'AlreadyAvailable' -extra @('/repair')
