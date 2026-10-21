#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Actual Windows SCM/System/restricted-token verification requires Windows'}
$Media=(Resolve-Path -LiteralPath $Media).Path; $Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'A new evidence directory is required'}
New-Item -ItemType Directory -Path $Output | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$release=Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw | ConvertFrom-Json
if($release.SourceCommit -notmatch '^[0-9a-f]{40}$'){throw 'Frozen source identity required'}
$inventory=@{}
foreach($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')){
 if($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$'){throw 'Invalid medium inventory'}
 $hash=$Matches[1];$relative=$Matches[2];$path=[IO.Path]::GetFullPath((Join-Path $Media $relative))
 if(-not $path.StartsWith($Media+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)){throw 'Unsafe or duplicate medium inventory'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw "Changed medium: $relative"};$inventory[$relative]=$hash
}
$fixture=& git -C $repo show ($release.SourceCommit+':tests/Ipc.Application.Verification/AdministratorBoundary.cs')
if($LASTEXITCODE -ne 0){throw 'Frozen service boundary fixture unavailable'}
[IO.File]::WriteAllLines((Join-Path $Output 'AdministratorBoundary.cs'),[string[]]$fixture)
[IO.File]::WriteAllText((Join-Path $Output 'Program.cs'),'if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); AdministratorBoundary.Run(args[0],args[1]);')
$references=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Ipc.*.dll')+@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Microsoft.Extensions.*.dll')
$xml=($references|ForEach-Object{'<Reference Include="'+$_.BaseName+'"><HintPath>'+[Security.SecurityElement]::Escape($_.FullName)+'</HintPath><Private>true</Private></Reference>'}) -join "`n"
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>Ipc.Application.Verification</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>'+$xml+'</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'Boundary.csproj'),$project)
& $Dotnet run --project (Join-Path $Output 'Boundary.csproj') -- $Media (Join-Path $Output 'synthetic') *> (Join-Path $Output 'RUN.log')
$result=$LASTEXITCODE
$unchanged=$true
foreach($relative in $inventory.Keys){if((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]){$unchanged=$false}}
[ordered]@{Status=if($result -eq 0 -and $unchanged){'PASS'}else{'FAIL'};SourceCommit=$release.SourceCommit;FixtureSourceCommit=$release.SourceCommit;UnchangedMedium=$unchanged;Scope='Actual SCM System service, protected credential/anchor ACL, real restricted Windows token, software authentication, lockout, expiry, ticket replay, missing-store and service-outage refusal; no equipment or production qualification';ExitCode=$result}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if($result -ne 0 -or -not $unchanged){Get-Content -LiteralPath (Join-Path $Output 'RUN.log') -Tail 24;throw 'Protected administrator boundary failed; original evidence retained'}
