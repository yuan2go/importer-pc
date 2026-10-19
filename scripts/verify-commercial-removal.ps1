#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$Media=(Resolve-Path -LiteralPath $Media).Path
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'New evidence directory required'}
$release=Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw|ConvertFrom-Json
if($release.SourceCommit -notmatch '^[0-9a-f]{40}$'){throw 'Frozen source identity required'}
$inventory=@{}
foreach($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')){
 if($line -notmatch '^([a-fA-F0-9]{64})  (.+)$'){throw 'Invalid inventory'}
 $hash=$Matches[1];$name=$Matches[2];$path=[IO.Path]::GetFullPath((Join-Path $Media $name))
 if(-not $path.StartsWith($Media+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($name)){throw 'Unsafe inventory'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Changed media'}
 $inventory[$name]=$hash
}
New-Item -ItemType Directory -Path $Output|Out-Null
$source=& git -C (Split-Path $PSScriptRoot -Parent) show ($release.SourceCommit+':tests/Ipc.Verification/CommercialRemovalVerification.cs')
if($LASTEXITCODE -ne 0){throw 'Frozen fixture unavailable'}
[IO.File]::WriteAllLines((Join-Path $Output 'CommercialRemovalVerification.cs'),[string[]]$source)
[IO.File]::WriteAllText((Join-Path $Output 'Program.cs'),'CommercialRemovalVerification.Run(Path.Combine(AppContext.BaseDirectory,"synthetic-removal"));')
$dll=Join-Path $Media 'app/Ipc.Platform.dll'
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><AssemblyName>Ipc.Verification</AssemblyName></PropertyGroup><ItemGroup><Reference Include="Ipc.Platform"><HintPath>'+[Security.SecurityElement]::Escape($dll)+'</HintPath></Reference></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'Removal.csproj'),$project)
& $Dotnet run --project (Join-Path $Output 'Removal.csproj') *> (Join-Path $Output 'RUN.log')
$result=$LASTEXITCODE
foreach($name in $inventory.Keys){if((Get-FileHash -LiteralPath (Join-Path $Media $name)).Hash -ne $inventory[$name]){throw 'Verification changed media'}}
[ordered]@{Status=if($result -eq 0){'PASS'}else{'FAIL'};SourceCommit=$release.SourceCommit;MediumInventorySha256=(Get-FileHash -LiteralPath (Join-Path $Media 'SHA256SUMS')).Hash;PlatformSha256=(Get-FileHash -LiteralPath $dll).Hash;Scope='Internal inventory kernel only; real Windows sharing violation with synthetic restart queue';ProtectedUninstall='NOT_RUN';ActualRebootDeletion='NOT_RUN';ExitCode=$result}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if($result -ne 0){throw 'Removal verification failed; evidence retained'}
