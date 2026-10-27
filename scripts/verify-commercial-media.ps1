#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media, [Parameter(Mandatory)][string]$Output, [string]$Dotnet = 'dotnet', [string]$FixtureCommit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This fixture uses the actual Windows WPF/DPAPI binaries; run on Windows. It does not provision runtime policy or operate equipment.' }
$repository = Split-Path $PSScriptRoot -Parent
$Media = (Resolve-Path -LiteralPath $Media).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Choose a new evidence directory' }
$release = Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw | ConvertFrom-Json
if ($release.ApplicationId -ne 'sportmaster' -or $release.SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Expected a frozen Sportmaster Windows medium' }
if (-not $FixtureCommit) { $FixtureCommit = $release.SourceCommit }
if ($FixtureCommit -notmatch '^[0-9a-f]{40}$') { throw 'FixtureCommit must be an explicit full Git commit identity' }
$inventory = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')) {
    if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') { throw 'Invalid frozen inventory' }
    $relative = $Matches[2]; $hash = $Matches[1]
    $path = [IO.Path]::GetFullPath((Join-Path $Media $relative))
    if (-not $path.StartsWith($Media + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)) { throw 'Unsafe/duplicate frozen inventory' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $hash) { throw "Changed medium: $relative" }
    $inventory[$relative] = $hash
}
New-Item -ItemType Directory -Path $Output | Out-Null
# Test source comes from an explicit fixed commit (medium by default); never from working files.
$fixture = & git -C $repository show ($FixtureCommit + ':tests/Ipc.Application.Verification/CommercialProductization.cs')
if ($LASTEXITCODE -ne 0) { throw 'Frozen fixture source is unavailable' }
[IO.File]::WriteAllLines((Join-Path $Output 'CommercialProductization.cs'), [string[]]$fixture)
$administration = & git -C $repository show ($FixtureCommit + ':tests/Ipc.Application.Verification/OperatorAdministration.cs')
if ($LASTEXITCODE -ne 0) { throw 'Frozen administrator/log fixture source is unavailable' }
[IO.File]::WriteAllLines((Join-Path $Output 'OperatorAdministration.cs'), [string[]]$administration)
foreach ($dependency in @('CurrentConfigurationFixture.cs', 'SyntheticPriorStorage.cs', 'CurrentParameterAuthorityChecks.cs')) {
    $source = & git -C $repository show ($FixtureCommit + ':tests/Ipc.Application.Verification/' + $dependency)
    if ($LASTEXITCODE -ne 0) { throw ('Frozen core fixture dependency is unavailable: ' + $dependency) }
    [IO.File]::WriteAllLines((Join-Path $Output $dependency), [string[]]$source)
}
$batch = & git -C $repository show ($FixtureCommit + ':tests/Ipc.Deployment.Verification/DeploymentBatchVerification.cs')
if ($LASTEXITCODE -ne 0) { throw 'Frozen deployment batch fixture source is unavailable' }
[IO.File]::WriteAllLines((Join-Path $Output 'DeploymentBatchVerification.cs'), [string[]]$batch)
$lifecycle = & git -C $repository show ($FixtureCommit + ':tests/Ipc.Application.Verification/LifecycleOptimization.cs')
if ($LASTEXITCODE -ne 0) { throw 'Frozen lifecycle review fixture source is unavailable' }
[IO.File]::WriteAllLines((Join-Path $Output 'LifecycleOptimization.cs'), [string[]]$lifecycle)
$entry = @'
using Ipc.Platform;
using Ipc.Assembly;
var media = Path.GetFullPath(args[0]); var root = Path.GetFullPath(args[1]); var evidence = Path.GetFullPath(args[2]);
var inventory = DeploymentMedia.Inspect(media, "sportmaster");
Directory.CreateDirectory(evidence);
await OperatorAdministration.Run(root, Path.Combine(evidence, "operator-admin"));
await CommercialProductization.Run(root, evidence);
var lifecycleEvidence = Path.Combine(evidence, "lifecycle-maintenance"); Directory.CreateDirectory(lifecycleEvidence);
await LifecycleOptimization.Run(root, lifecycleEvidence);
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
var request = ParcelStartupServices.Review(inventory, identity.User!.Value, Path.Combine(evidence, "batch-new-data"), Path.Combine(media, "app", "config"), null, [], false)
    with { Shortcuts = new(false, false) };
var planFile = Path.Combine(evidence, "deployment-plan.json"); File.WriteAllText(planFile, Wire.Encode(DeploymentBatch.Freeze(request)));
var receipt = Path.Combine(evidence, "preflight.json");
if (!DeploymentBatch.Handle(["--deployment-plan", planFile, "--receipt", receipt, "--preflight"]) || Environment.ExitCode != 0) throw new Exception("Frozen media batch preflight failed");
var receiptHash = Wire.FileHash(receipt);
_ = DeploymentBatch.Handle(["--deployment-plan", planFile, "--receipt", receipt, "--preflight"]);
if (Environment.ExitCode == 0 || Wire.FileHash(receipt) != receiptHash) throw new Exception("Batch overwrote original receipt");
Environment.ExitCode = 0;
DeploymentBatchVerification.Run(inventory, Path.Combine(evidence, "batch-regressions"));
Console.WriteLine("PASS frozen native batch preflight and duplicate receipt refusal; no installation/device startup");
'@
[IO.File]::WriteAllText((Join-Path $Output 'Program.cs'), $entry)
$references = @(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Ipc.*.dll') + @(Get-Item -LiteralPath (Join-Path $Media 'desktop/Ipc.Application.ParcelFlow.Desktop.dll'))
$xmlReferences = ($references | ForEach-Object { '<Reference Include="' + [Security.SecurityElement]::Escape($_.BaseName) + '"><HintPath>' + [Security.SecurityElement]::Escape($_.FullName) + '</HintPath><Private>true</Private></Reference>' }) -join "`n"
$sqlite = @(@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Microsoft.Data.Sqlite*.dll') + @(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'SQLitePCLRaw*.dll') | Sort-Object FullName -Unique)
$xmlReferences += ($sqlite | ForEach-Object { '<Reference Include="' + $_.BaseName + '"><HintPath>' + [Security.SecurityElement]::Escape($_.FullName) + '</HintPath><Private>true</Private></Reference>' }) -join "`n"
$nativeSqlite = [Security.SecurityElement]::Escape((Join-Path $Media 'app/e_sqlite3.dll'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>Ipc.Application.Verification</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><Using Include="System.IO"/>' + $xmlReferences + '<Content Include="' + $nativeSqlite + '" Link="e_sqlite3.dll" CopyToOutputDirectory="Always"/></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'MediaVerification.csproj'), $project)
$fixtureWorkspace = Join-Path $Output 'workspace'
New-Item -ItemType Directory -Path (Join-Path $fixtureWorkspace 'config/sportmaster') | Out-Null
Copy-Item -LiteralPath (Join-Path $Media 'app/config/business.json') -Destination (Join-Path $fixtureWorkspace 'config/sportmaster/business.json')
& $Dotnet run --project (Join-Path $Output 'MediaVerification.csproj') -- $Media $fixtureWorkspace (Join-Path $Output 'synthetic-data') *> (Join-Path $Output 'RUN.log')
$result = $LASTEXITCODE
$unchanged = $true
foreach ($relative in $inventory.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]) { $unchanged = $false } }
$binaries = @($references | ForEach-Object { [ordered]@{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
[ordered]@{Status=if ($result -eq 0 -and $unchanged) {'PASS'} else {'FAIL'};SourceCommit=$release.SourceCommit;FixtureCommit=$FixtureCommit;MediumInventorySha256=(Get-FileHash -LiteralPath (Join-Path $Media 'SHA256SUMS')).Hash;UnchangedMedium=$unchanged;Binaries=$binaries;Scope='SYNTHETIC_APPLICATION_CORE_REAL_PUBLISHED_DLLS';WindowsInstallation='NOT_RUN';Equipment='BLOCKED';ExitCode=$result} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if ($result -ne 0 -or -not $unchanged) { throw 'Frozen-medium verification failed; retained RUN.log and original evidence' }
