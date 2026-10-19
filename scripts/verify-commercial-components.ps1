#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Package,[Parameter(Mandatory)][string]$Trust,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'This bounded native component fixture requires Windows; it never installs or activates a provider'}
$Media=(Resolve-Path -LiteralPath $Media).Path
$Package=(Resolve-Path -LiteralPath $Package).Path
$Trust=(Resolve-Path -LiteralPath $Trust).Path
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'New evidence directory required'}
$release=Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw | ConvertFrom-Json
if($release.ApplicationId -ne 'sportmaster' -or $release.SourceCommit -notmatch '^[0-9a-f]{40}$'){throw 'Expected frozen Sportmaster medium'}
$inventory=@{}
foreach($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')){
 if($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$'){throw 'Invalid inventory'}
 $hash=$Matches[1];$relative=$Matches[2];$path=[IO.Path]::GetFullPath((Join-Path $Media $relative))
 if(-not $path.StartsWith($Media+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)){throw 'Unsafe/duplicate inventory'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw "Changed medium: $relative"};$inventory[$relative]=$hash
}
$packageHash=(Get-FileHash -LiteralPath $Package).Hash;$trustHash=(Get-FileHash -LiteralPath $Trust).Hash
New-Item -ItemType Directory -Path $Output | Out-Null
$fixture=& git -C (Split-Path $PSScriptRoot -Parent) show ($release.SourceCommit+':tests/Ipc.Application.Verification/CommercialComponentReview.cs')
if($LASTEXITCODE -ne 0){throw 'Missing frozen component fixture'}
[IO.File]::WriteAllLines((Join-Path $Output 'CommercialComponentReview.cs'),[string[]]$fixture)
[IO.File]::WriteAllText((Join-Path $Output 'Program.cs'),'CommercialComponentReview.Run(Path.Combine(Path.GetFullPath(args[0]),"work"),Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));')
$refs=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Ipc.*.dll')
$xml=($refs|ForEach-Object{'<Reference Include="'+$_.BaseName+'"><HintPath>'+[Security.SecurityElement]::Escape($_.FullName)+'</HintPath><Private>true</Private></Reference>'}) -join "`n"
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>Ipc.Application.Verification</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><Using Include="System.IO"/>'+$xml+'</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'Review.csproj'),$project)
& $Dotnet run --project (Join-Path $Output 'Review.csproj') -- $Output $Package $Trust *> (Join-Path $Output 'RUN.log')
$code=$LASTEXITCODE
foreach($relative in $inventory.Keys){if((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]){throw "Verification changed medium: $relative"}}
if((Get-FileHash -LiteralPath $Package).Hash -ne $packageHash -or (Get-FileHash -LiteralPath $Trust).Hash -ne $trustHash){throw 'Verification changed input package/trust'}
[ordered]@{Status=if($code -eq 0){'PASS'}else{'FAIL'};SourceCommit=$release.SourceCommit;FixtureCommit=$release.SourceCommit;MediumInventorySha256=(Get-FileHash -LiteralPath (Join-Path $Media 'SHA256SUMS')).Hash;PackageSha256=$packageHash;TrustSha256=$trustHash;UnchangedInputs=$true;ExitCode=$code;Scope='Simulation/TestOnly independent reference module; static signatures/closure only; no provider load/install/activation or device startup'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if($code -ne 0){throw 'Frozen component review failed; original log retained'}
