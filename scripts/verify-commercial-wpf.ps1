#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'verification-evidence.ps1')
if (-not $IsWindows) { throw 'Run native WPF verification on Windows; it does not provision policy or operate equipment' }
$Media=(Resolve-Path -LiteralPath $Media).Path
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'New evidence directory required'}
$repo=Split-Path $PSScriptRoot -Parent
$release=Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw | ConvertFrom-Json
if ($release.ApplicationId -ne 'sportmaster' -or $release.SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Expected a frozen Sportmaster Windows medium' }
$fixtureSource=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $release.SourceCommit -ne $fixtureSource){throw 'Only media built from the current commit can be verified; historical evidence remains frozen'}
New-Item -ItemType Directory -Path $Output | Out-Null
$inventory=@{}
foreach($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')) {
 if($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$'){throw 'Invalid frozen inventory'}
 $hash=$Matches[1]; $relative=$Matches[2]; $path=[IO.Path]::GetFullPath((Join-Path $Media $relative))
 if(-not $path.StartsWith($Media+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)){throw 'Unsafe/duplicate inventory'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw "Changed medium: $relative"}
 $inventory[$relative]=$hash
}
Assert-MediumInventory $Media $inventory
$fontAssembly = Join-Path $Media 'desktop/Ipc.Presentation.Wpf.dll'
if (-not $inventory.ContainsKey('desktop/Ipc.Presentation.Wpf.dll')) { throw 'Frozen WPF assembly is missing from the verified inventory' }
foreach ($source in @('Typography.cs', 'ScreenshotCapture.cs')) {
 $content = & git -C $repo show ($fixtureSource + ':tests/Ipc.Desktop.UiVerification/' + $source)
 if ($LASTEXITCODE -ne 0) { throw "Missing frozen native fixture: $source" }
 [IO.File]::WriteAllLines((Join-Path $Output $source), [string[]]$content)
}
$reference = [Security.SecurityElement]::Escape($fontAssembly)
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
 </PropertyGroup>
 <ItemGroup>
  <Using Include="System.IO"/>
  <Reference Include="Ipc.Presentation.Wpf"><HintPath>$reference</HintPath></Reference>
 </ItemGroup>
</Project>
"@
$projectPath = Join-Path $Output 'NativeMedia.csproj'
[IO.File]::WriteAllText($projectPath, $project)
@"
using System.Windows;

static partial class Program
{
    [STAThread]
    static int Main()
    {
        var evidence = Environment.GetEnvironmentVariable("IPC_UI_EVIDENCE")!;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var lines = new List<string>();
        app.Startup += async (_, _) =>
        {
            var result = 0;
            try { await VerifyTypography(evidence, lines); }
            catch (Exception error) { lines.Add(error.ToString()); result = 1; }
            finally { File.WriteAllLines(Path.Combine(evidence, "WINDOWS-WPF.log"), lines); app.Shutdown(result); }
        };
        return app.Run();
    }
}
"@ | Set-Content -LiteralPath (Join-Path $Output 'Program.cs') -Encoding utf8NoBOM
$previousEvidence = $env:IPC_UI_EVIDENCE
try {
 $env:IPC_UI_EVIDENCE = $Output
 & $Dotnet run --project $projectPath *> (Join-Path $Output 'RUN.log')
 $result = $LASTEXITCODE
} finally { $env:IPC_UI_EVIDENCE = $previousEvidence }
foreach ($relative in $inventory.Keys) {
 if ((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]) { throw "Verification changed medium: $relative" }
}
[ordered]@{
 Status = if ($result -eq 0) { 'PASS' } else { 'FAIL' }
 SourceCommit = $release.SourceCommit
 FixtureSourceCommit = $fixtureSource
 FixtureMatchesMedium = ($fixtureSource -eq $release.SourceCommit)
 MediumInventorySha256 = (Get-FileHash -LiteralPath (Join-Path $Media 'SHA256SUMS')).Hash
 Scope = 'Actual WPF glyph resolution and font bytes from the frozen published assembly'
 ExitCode = $result
 Binaries = @([ordered]@{ File = 'Ipc.Presentation.Wpf.dll'; Sha256 = (Get-FileHash -LiteralPath $fontAssembly).Hash })
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if ($result -ne 0) {
 Get-Content -LiteralPath (Join-Path $Output 'RUN.log') -Tail 24
 throw 'Frozen WPF typography verification failed'
}
