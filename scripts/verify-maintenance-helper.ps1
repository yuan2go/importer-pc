#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
if(-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') { throw 'Only the disposable hosted Windows CI runner may perform this real installation fixture' }
$Media=(Resolve-Path -LiteralPath $Media).Path; $Output=[IO.Path]::GetFullPath($Output)
if(-not $Output.StartsWith('D:\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $Output)){throw 'A new D evidence directory is required'}
$repo=Split-Path $PSScriptRoot -Parent
$release=Get-Content -LiteralPath (Join-Path $Media 'app/application-release.json') -Raw | ConvertFrom-Json
$head=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $release.SourceCommit -notmatch '^[0-9a-f]{40}$' -or $release.SourceCommit -ne $head){throw 'Current frozen source identity required'}
$inventory=@{}
foreach($line in Get-Content -LiteralPath (Join-Path $Media 'SHA256SUMS')){
 if($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$'){throw 'Invalid medium inventory'}
 $hash=$Matches[1];$relative=$Matches[2];$path=[IO.Path]::GetFullPath((Join-Path $Media $relative))
 if(-not $path.StartsWith($Media+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)){throw 'Unsafe or duplicate medium inventory'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw "Changed medium: $relative"};$inventory[$relative]=$hash
}
New-Item -ItemType Directory -Path $Output | Out-Null
foreach($entry in @(@('tests/Ipc.Application.Verification/MaintenanceHelperWindows.cs','MaintenanceHelperWindows.cs'),@('tests/Ipc.Application.Verification/sportmaster.synthetic.json','synthetic.json'),@('global.json','global.json'))){
 $source=& git -C $repo show ($release.SourceCommit+':'+$entry[0])
 if($LASTEXITCODE -ne 0){throw 'Frozen maintenance fixture unavailable'}
 [IO.File]::WriteAllLines((Join-Path $Output $entry[1]),[string[]]$source)
}
[IO.File]::WriteAllText((Join-Path $Output 'Program.cs'),'if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); await MaintenanceHelperWindows.Run(args[0],args[1],args[2]);')
$references=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Ipc.*.dll')+@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Microsoft.Extensions.*.dll')+@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Microsoft.Data.Sqlite*.dll')+@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'SQLitePCLRaw*.dll')
$xml=($references|Sort-Object FullName -Unique|ForEach-Object{'<Reference Include="'+$_.BaseName+'"><HintPath>'+[Security.SecurityElement]::Escape($_.FullName)+'</HintPath><Private>true</Private></Reference>'}) -join "`n"
$native=[Security.SecurityElement]::Escape((Join-Path $Media 'app/e_sqlite3.dll'))
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><DefineConstants>FROZEN_MAINTENANCE_BOUNDARY</DefineConstants><AssemblyName>Ipc.Application.Verification</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>'+$xml+'<Content Include="'+$native+'" Link="e_sqlite3.dll" CopyToOutputDirectory="Always"/></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'MaintenanceBoundary.csproj'),$project)
& $Dotnet run --project (Join-Path $Output 'MaintenanceBoundary.csproj') -- $Media (Join-Path $Output 'synthetic') (Join-Path $Output 'synthetic.json') *> (Join-Path $Output 'RUN.log')
$result=$LASTEXITCODE;$unchanged=$true
foreach($relative in $inventory.Keys){if((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]){$unchanged=$false}}
$passes=@(Select-String -LiteralPath (Join-Path $Output 'RUN.log') -Pattern '^PASS WT048-HELPER ').Count
[ordered]@{Status=if($result -eq 0 -and $unchanged){'PASS'}else{'FAIL'};SourceCommit=$release.SourceCommit;FixtureSourceCommit=$release.SourceCommit;UnchangedMedium=$unchanged;Passed=$passes;Scope='Actual protected Windows helper, original PID/request binding, cancelled pre-Begin, late installation cancellation, same-source repair, complete backup, isolated restore, IO refusal, malformed control/disconnection, uninstall retaining original data';NotRun=@('Interactive UAC','Real devices or SDK','Second-version upgrade medium','Recovery-enabled system repair');ExitCode=$result}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if($result -ne 0 -or -not $unchanged){Get-Content -LiteralPath (Join-Path $Output 'RUN.log') -Tail 32;throw 'Protected maintenance boundary failed; original evidence retained'}
