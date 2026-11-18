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

& "$PSScriptRoot/initialize-disposable-windows-volume.ps1"

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
# Use the actual packaged implementation and exact activation manifest. Merely
# finding some VC90 files in WinSxS does not prove this binding can activate.
$probeRoot=Join-Path $evidence 'vc90-activation-probe'
New-Item -ItemType Directory -Path $probeRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $probeRoot 'Probe.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $probeRoot 'Program.cs'),'var assembly=System.Reflection.Assembly.LoadFrom(args[0]);var method=assembly.GetType("Ipc.Platform.SmVcRuntimeSetup",true).GetMethod("CanLoadVc90",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);System.Console.WriteLine((bool)method.Invoke(null,new object[]{args[1]})?"Available":"Missing");')
& dotnet build (Join-Path $probeRoot 'Probe.csproj') --nologo *> (Join-Path $evidence 'vc90-probe-build.log')
if($LASTEXITCODE){throw 'Could not build the read-only actual activation probe'}
$activationManifest=Join-Path $verificationRoot 'media/prerequisites/vc90-probe.manifest'
$availability=& dotnet (Join-Path $probeRoot 'bin/Debug/net10.0/Probe.dll') (Join-Path $verificationRoot 'media/app/Ipc.Platform.dll') $activationManifest
if($LASTEXITCODE -ne 0 -or $availability -notin 'Available','Missing'){throw 'Actual VC90 activation probe failed'}
$expectedVc90=if($availability -eq 'Available'){'AlreadyAvailable'}else{'Installed'}
[ordered]@{Expected=$expectedVc90;ActualActivation=$availability;ManifestSha256=(Get-FileHash -LiteralPath $activationManifest).Hash;PlatformSha256=(Get-FileHash -LiteralPath (Join-Path $verificationRoot 'media/app/Ipc.Platform.dll')).Hash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'vc90-activation.json')
Write-Output "PASS baseline: VC++ 2010 absent; exact packaged VC90 activation=$availability; expected=$expectedVc90."

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

# Exercise startup dependency repair without starting cameras, RCS or business data.
# Only this disposable runner removes a real runtime and an installed decoder DLL.
$registration = 'D:/ImporterPc/Machine/deployments/sportmaster.json'
$deployment = Get-Content -LiteralPath $registration -Raw | ConvertFrom-Json
$probeRoot = Join-Path $verificationRoot 'dependency-probe'
New-Item -ItemType Directory -Path $probeRoot | Out-Null
$platformReference = [Security.SecurityElement]::Escape((Join-Path $deployment.ReleaseRoot 'app/Ipc.Platform.dll'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><Reference Include="Ipc.Platform"><HintPath>$platformReference</HintPath></Reference></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath "$probeRoot/Probe.csproj"
@'
using Ipc.Platform;
var location = WindowsDeploymentSetup.Current("sportmaster")!;
var desktop = Path.Combine(location.ReleaseRoot, "desktop");
var registration = WindowsDeploymentSetup.Registration("sportmaster");
var original = Wire.FileHash(registration);
var seed = Path.Combine(location.ConfigurationDirectory, "business.json");
var seedHash = Wire.FileHash(seed);
var marker = Path.Combine(location.DataRoot, "dependency-repair-synthetic-marker");
File.WriteAllText(marker, "original synthetic data retained");
var markerHash = Wire.FileHash(marker);
var before = WindowsDeploymentSetup.InspectCameraDependencies(desktop);
if (!before.Files.Contains("desktop/vendor/zxing-cpp/ZXing.dll") || !before.Runtimes.Contains("Visual C++ 2010 SP1 x64"))
    throw new Exception("Startup inspection failed to find the missing DLL and real VC++ runtime");
WindowsDeploymentSetup.RepairCameraDependencies(original);
if (WindowsDeploymentSetup.InspectCameraDependencies(desktop).NeedsRepair || Wire.FileHash(registration) != original ||
    Wire.FileHash(seed) != seedHash || Wire.FileHash(marker) != markerHash)
    throw new Exception("Startup dependency repair did not preserve the installation/configuration/data identity");
Console.WriteLine("PASS STARTUP-DEPENDENCIES missing DLL and real VC++ runtime recovered; original registration, configuration and data retained; no business startup");
'@ | Set-Content -LiteralPath "$probeRoot/Program.cs"
dotnet build "$probeRoot/Probe.csproj" -c Release -v quiet > "$evidence/build-dependency-probe.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/build-dependency-probe.log" -Tail 20; throw 'Dependency probe build failed' }
$decoder = Join-Path $deployment.ReleaseRoot 'desktop/vendor/zxing-cpp/ZXing.dll'
if (![IO.Path]::GetFullPath($decoder).StartsWith([IO.Path]::GetFullPath($deployment.ReleaseRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Synthetic missing-file target escaped the installed release'
}
Remove-Item -LiteralPath $decoder
$vc100Products = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match '^Microsoft Visual C\+\+ 2010\b.*x64' -and $_.PSChildName -match '^\{[0-9a-fA-F-]{36}\}$' }
foreach ($product in $vc100Products) {
    $removed = Start-Process msiexec.exe -ArgumentList @('/x', $product.PSChildName, '/qn', '/norestart') -PassThru -Wait -WindowStyle Hidden
    if ($removed.ExitCode -notin 0,1605,3010) { throw 'Could not remove the disposable VC++ 2010 runtime' }
}
if (Test-Path -LiteralPath $systemRuntime) { throw 'Startup repair requires the real VC++ 2010 runtime to be missing' }
dotnet "$probeRoot/bin/Release/net10.0-windows/Probe.dll" > "$evidence/startup-dependency-repair.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/startup-dependency-repair.log" -Tail 25; throw 'Startup dependency recovery failed' }
Get-Content "$evidence/startup-dependency-repair.log"
# A registered product with one missing C++ DLL must also be repaired, rather
# than treating the still-loadable C runtime or product registration as healthy.
Remove-Item -LiteralPath (Join-Path $env:WINDIR 'System32/msvcp100.dll')
Remove-Item -LiteralPath $decoder
dotnet "$probeRoot/bin/Release/net10.0-windows/Probe.dll" > "$evidence/startup-partial-runtime-repair.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/startup-partial-runtime-repair.log" -Tail 25; throw 'Partial runtime repair failed' }
Get-Content "$evidence/startup-partial-runtime-repair.log"

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

# Exercise installed apphosts and protected registrations in this disposable
# image. No camera, RCS, primary business instance or business database is started.
$operationsProbeSource = @'
using System.Diagnostics;
using Ipc.Platform;
var location = WindowsDeploymentSetup.Current("sportmaster")!;
var desktop = Path.Combine(location.ReleaseRoot, "desktop");
if (!File.Exists(Path.Combine(desktop, InstallationIdentity.DesktopExecutable)) ||
    Directory.GetFiles(desktop, "Ipc.Operations.Desktop*.exe").Length != 0) throw new Exception("Invalid installed SM entry set");
var registration = WindowsDeploymentSetup.Registration("sportmaster");
var before = Wire.FileHash(registration);
var databases = Directory.GetFiles(location.DataRoot, "*.db", SearchOption.AllDirectories);
async Task<Process> Open()
{
    var child = Process.Start(new ProcessStartInfo(Path.Combine(desktop, InstallationIdentity.DesktopExecutable), InstallationIdentity.OperationsArgument)
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true })!;
    try
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            child.Refresh();
            if (child.HasExited) throw new Exception("Installed operations exited before its window: " + child.ExitCode + ": " + await child.StandardError.ReadToEndAsync());
            if (child.MainWindowHandle != IntPtr.Zero && child.MainWindowTitle == InstallationIdentity.OperationsDisplayName) return child;
            await Task.Delay(50);
        }
        throw new TimeoutException("Installed operations window did not open");
    }
    catch { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } child.Dispose(); throw; }
}
async Task Released()
{
    var deadline = Stopwatch.StartNew();
    while (true)
    {
        try { using var maintenance = DeploymentProgramUse.Maintain(location); return; }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25); }
    }
}
using (var child = await Open())
{
    try
    {
        using (var business = ApplicationRootOwnership.AcquireRuntime("sportmaster", location.DataRoot)) { }
        var blocked = false;
        try { using var maintenance = DeploymentProgramUse.Maintain(location); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { blocked = true; }
        if (!blocked) throw new Exception("Live installed observer did not protect program files");
        if (!child.CloseMainWindow()) throw new Exception("Could not request normal operations close");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (child.ExitCode != 0) throw new Exception("Operations did not close normally: " + child.ExitCode + ": " + await child.StandardError.ReadToEndAsync());
    }
    finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
}
await Released();
using (var child = await Open()) { child.Kill(true); await child.WaitForExitAsync(); }
await Released();
if (before != Wire.FileHash(registration) || !databases.SequenceEqual(Directory.GetFiles(location.DataRoot, "*.db", SearchOption.AllDirectories)))
    throw new Exception("Operations changed registration or created a business database");
Console.WriteLine("PASS INSTALLED-OPERATIONS " + InstallationIdentity.DisplayName + ": single application executable with operations argument; native offline tool; normal/crash file-use release; business owner remains free; no database startup");
'@
function Verify-InstalledOperations([string]$mediaRoot, [string]$name) {
    $probe = Join-Path $evidence "installed-operations-$name"
    New-Item -ItemType Directory -Path $probe | Out-Null
    $platform = Join-Path $mediaRoot 'app/Ipc.Platform.dll'
    "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><Reference Include=`"Ipc.Platform`"><HintPath>$platform</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath "$probe/Probe.csproj"
    $operationsProbeSource | Set-Content -LiteralPath "$probe/Program.cs"
    dotnet build "$probe/Probe.csproj" -c Release -v quiet > "$probe/build.log" 2>&1
    if ($LASTEXITCODE) { Get-Content "$probe/build.log" -Tail 20; throw 'Installed operations probe build failed' }
    dotnet "$probe/bin/Release/net10.0-windows/Probe.dll" > "$probe/result.log" 2>&1
    if ($LASTEXITCODE) { Get-Content "$probe/result.log" -Tail 25; throw 'Actual installed operations verification failed' }
    Get-Content "$probe/result.log"
}
Verify-InstalledOperations -mediaRoot "$verificationRoot/media" -name 'ordinary'
$formalRegistrationHash = (Get-FileHash -LiteralPath $registration).Hash
$testArea = 'D:/ProgramData/ImporterPcTest'
# This fixture creates the default data parent on the disposable image too.
# Protecting only its child cannot establish a trustworthy ancestor chain.
if (!(Test-Path -LiteralPath 'D:/ProgramData')) {
    New-Item -ItemType Directory -Path 'D:/ProgramData' | Out-Null
    Set-Acl -LiteralPath 'D:/ProgramData' -AclObject $protected
}
New-Item -ItemType Directory -Path "$testArea/DeploymentLogs" -Force | Out-Null
Set-Acl -LiteralPath $testArea -AclObject $protected
Set-Acl -LiteralPath "$testArea/DeploymentLogs" -AclObject $protected
# Exercise the original installed two-apphost format through a real same-version
# quiet upgrade, including its protected inventory and actual common-menu link.
$legacySha = 'ce10a16b216bbc936156ef05d7e9074f5a231e9e'
$legacySource = Join-Path $verificationRoot 'previous-source'
git fetch --no-tags origin $legacySha
if ($LASTEXITCODE) { throw 'Cannot fetch the pinned dual-apphost baseline' }
git -c core.longpaths=true -c core.protectNTFS=false worktree add -b ci/legacy-operations $legacySource $legacySha
if ($LASTEXITCODE) { throw 'Cannot check out the pinned dual-apphost baseline' }
Push-Location $legacySource
try {
    python scripts/package-application.py --application sportmaster --target win-x64 --output "$verificationRoot/previous-media" --source-branch ci/legacy-operations --test-instance --verification-without-vendor-runtime --vc-redist $inputRoot > "$evidence/build-previous-media.log" 2>&1
    if ($LASTEXITCODE) { Get-Content "$evidence/build-previous-media.log" -Tail 20; throw 'Previous medium build failed' }
    python scripts/package-installer.py --media "$verificationRoot/previous-media.zip" --output "$verificationRoot/previous-installer" --test-instance > "$evidence/build-previous-installer.log" 2>&1
    if ($LASTEXITCODE) { Get-Content "$evidence/build-previous-installer.log" -Tail 20; throw 'Previous installer build failed' }
} finally { Pop-Location }
$previousInstaller = (Get-ChildItem -LiteralPath "$verificationRoot/previous-installer" -File -Filter '*.exe' | Select-Object -First 1).FullName
$previousProcess = Start-Process -FilePath $previousInstaller -ArgumentList @('/quiet','/norestart',"/operationid=$([Guid]::NewGuid().ToString('N'))","/log=$testArea/DeploymentLogs/previous-install.jsonl") -PassThru -Wait -WindowStyle Hidden
if ($previousProcess.ExitCode) { throw "Previous test installation failed: $($previousProcess.ExitCode)" }
$menuLink = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) '菜鸟供包台（测试）/供包运维工具（测试）.lnk'
function Read-OperationsLink {
    if (!(Test-Path -LiteralPath $menuLink)) { throw 'Installed operations menu link missing' }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($menuLink)
        try { return @{ Target = $link.TargetPath; Arguments = $link.Arguments } }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}
$beforeLink = Read-OperationsLink
if ((Split-Path $beforeLink.Target -Leaf) -ne 'Ipc.Operations.Desktop.Test.exe' -or $beforeLink.Arguments -or !(Test-Path -LiteralPath $beforeLink.Target)) { throw 'Previous deployment has no usable legacy operations link' }
python scripts/package-application.py --application sportmaster --target win-x64 --output "$verificationRoot/test-media" --source-branch ci/runtime-install --test-instance --verification-without-vendor-runtime --vc-redist $inputRoot > "$evidence/build-test-media.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/build-test-media.log" -Tail 20; throw 'Test-identity two-program medium build failed' }
python scripts/package-installer.py --media "$verificationRoot/test-media.zip" --output "$verificationRoot/test-installer" --test-instance > "$evidence/build-test-installer.log" 2>&1
if ($LASTEXITCODE) { Get-Content "$evidence/build-test-installer.log" -Tail 20; throw 'Test-identity installer build failed' }
$testInstaller = (Get-ChildItem -LiteralPath "$verificationRoot/test-installer" -File -Filter '*.exe' | Select-Object -First 1).FullName
$testOperation = [Guid]::NewGuid().ToString('N')
$testProcess = Start-Process -FilePath $testInstaller -ArgumentList @('/quiet', '/norestart', "/operationid=$testOperation", "/log=$testArea/DeploymentLogs/install.jsonl") -PassThru -Wait -WindowStyle Hidden -RedirectStandardError "$evidence/test-install-stderr.log"
if (Test-Path -LiteralPath "$testArea/DeploymentLogs/install.jsonl") { Copy-Item -LiteralPath "$testArea/DeploymentLogs/install.jsonl" -Destination "$evidence/test-install.jsonl" }
if ($testProcess.ExitCode) {
    Get-Content -LiteralPath "$evidence/test-install-stderr.log" -Tail 12
    if (Test-Path -LiteralPath "$evidence/test-install.jsonl") { Get-Content -LiteralPath "$evidence/test-install.jsonl" -Tail 12 }
    throw "Test installation failed: $($testProcess.ExitCode)"
}
$updatedLink = Read-OperationsLink
if ((Split-Path $updatedLink.Target -Leaf) -ne 'Ipc.Desktop.Test.exe' -or $updatedLink.Arguments -ne '--operations' -or !(Test-Path -LiteralPath $updatedLink.Target)) { throw 'Upgrade did not replace the legacy menu link' }
Write-Output 'PASS same-version dual-apphost to single-apphost overlay and native shortcut replacement'
Verify-InstalledOperations -mediaRoot "$verificationRoot/test-media" -name 'test'
if ((Get-FileHash -LiteralPath $registration).Hash -ne $formalRegistrationHash) { throw 'Test installation changed the ordinary deployment registration' }
$testProcess = Start-Process -FilePath $testInstaller -ArgumentList @('/quiet', '/uninstall', '/norestart', "/operationid=$([Guid]::NewGuid().ToString('N'))", "/log=$testArea/DeploymentLogs/uninstall.jsonl") -PassThru -Wait -WindowStyle Hidden
if (Test-Path -LiteralPath "$testArea/DeploymentLogs/uninstall.jsonl") { Copy-Item -LiteralPath "$testArea/DeploymentLogs/uninstall.jsonl" -Destination "$evidence/test-uninstall.jsonl" }
if ($testProcess.ExitCode) {
    if (Test-Path -LiteralPath "$evidence/test-uninstall.jsonl") { Get-Content -LiteralPath "$evidence/test-uninstall.jsonl" -Tail 12 }
    throw "Independent test uninstall failed: $($testProcess.ExitCode)"
}
if ((Get-FileHash -LiteralPath $registration).Hash -ne $formalRegistrationHash -or !(Test-Path -LiteralPath (Join-Path $deployment.ReleaseRoot 'desktop/Ipc.Desktop.exe'))) {
    throw 'Independent test uninstall changed the ordinary installation'
}
Write-Output 'PASS TWO-INSTALLATIONS ordinary/test two-EXE identities coexist; each tool uses its own protected registration; test uninstall preserves ordinary deployment.'
