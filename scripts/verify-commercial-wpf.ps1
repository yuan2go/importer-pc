#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet',[switch]$VisualProductization,[switch]$Interactive,[switch]$AuditPerformance,[switch]$ParameterV3,[switch]$ManagementMode,[switch]$CleanInstallBootstrap,[switch]$StartupExperience,[switch]$ParameterGuidanceStartup,[switch]$ParameterShellPolish,[switch]$TaskHierarchy,[switch]$Win11Stability,[switch]$ReliabilityLogs,[switch]$WorkstationStatusLogs,[switch]$OperatorWorkspace,[switch]$QueryFilters,[switch]$DebugLogLive,[switch]$StabilitySeedUpgrade,[switch]$InstallerFirstRunUi,[switch]$LifecycleUx)
$ErrorActionPreference='Stop'
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
$refs=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Ipc.*.dll')+@(Get-Item -LiteralPath (Join-Path $Media 'desktop/Ipc.Application.ParcelFlow.Desktop.dll'))+@(Get-Item -LiteralPath (Join-Path $Media 'desktop/Ipc.Application.Sportmaster.Desktop.dll'))+@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'Microsoft.Data.Sqlite*.dll')
$refs+=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app') -Filter 'SQLitePCLRaw*.dll')
$refs+=@(Get-Item -LiteralPath (Join-Path $Media 'desktop/Ipc.Desktop.dll'))
$typography=Join-Path $Media 'desktop/Ipc.Presentation.Wpf.dll'
if(Test-Path -LiteralPath $typography){$refs+=@(Get-Item -LiteralPath $typography)}
$xml=($refs | Sort-Object FullName -Unique | ForEach-Object {'<Reference Include="'+$_.BaseName+'"><HintPath>'+[Security.SecurityElement]::Escape($_.FullName)+'</HintPath><Private>true</Private></Reference>'}) -join "`n"
$sourcePaths=@(& git -C $repo ls-tree -r --name-only $fixtureSource -- tests/Ipc.Desktop.UiVerification)
if($LASTEXITCODE -ne 0){throw 'Frozen native fixture tree is unavailable'}
# Program dispatch and its partial implementations must come from one complete
# pinned fixture. A hand-maintained subset omitted PARAM-01 and the V2 journeys.
$sources=@($sourcePaths | Where-Object { $_ -match '^tests/Ipc.Desktop.UiVerification/[^/]+\.cs$' } | ForEach-Object { [IO.Path]::GetFileName($_) })
if($sources -notcontains 'Program.cs'){throw 'Frozen native fixture has no entry point'}
$hasPerformance=$sources -contains 'AuditPerformance.cs'
if($AuditPerformance -and -not $hasPerformance){throw 'This frozen source has no audit performance fixture'}
foreach($source in $sources){
 $content=& git -C $repo show ($fixtureSource+':tests/Ipc.Desktop.UiVerification/'+$source)
 if($LASTEXITCODE -ne 0){throw 'Missing fixed native fixture'}
 [IO.File]::WriteAllLines((Join-Path $Output $source),[string[]]$content)
}
foreach ($coreSource in @('CommercialProductization.cs','CurrentConfigurationFixture.cs','NonCurrentStorageFixture.cs')) {
 $core=& git -C $repo show ($fixtureSource+':tests/Ipc.Application.Verification/'+$coreSource)
 if($LASTEXITCODE -ne 0){throw ('Missing frozen native fixture dependency: '+$coreSource)}
 [IO.File]::WriteAllLines((Join-Path $Output $coreSource),[string[]]$core)
}
New-Item -ItemType Directory -Path (Join-Path $Output 'Themes') | Out-Null
$theme=& git -C $repo show ($fixtureSource+':src/Ipc.Desktop/Themes/Workstation.xaml')
[IO.File]::WriteAllLines((Join-Path $Output 'Themes/Workstation.xaml'),[string[]]$theme)
$native=[Security.SecurityElement]::Escape((Join-Path $Media 'app/e_sqlite3.dll'))
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>Ipc.Desktop.UiVerification</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><Using Include="System.IO"/>'+$xml+'<Content Include="'+$native+'" Link="e_sqlite3.dll" CopyToOutputDirectory="Always"/></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $Output 'NativeMedia.csproj'),$project)
$workspace=Join-Path $Output 'workspace'
New-Item -ItemType Directory -Path (Join-Path $workspace 'config/sportmaster') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $workspace '.artifacts') | Out-Null
Copy-Item -LiteralPath (Join-Path $Media 'app/config/business.json') -Destination (Join-Path $workspace 'config/sportmaster/business.json')
$syntheticConfiguration = & git -C $repo show ($fixtureSource + ':tests/Ipc.Application.Verification/sportmaster.synthetic.json')
if ($LASTEXITCODE -ne 0) { throw 'Exact synthetic current configuration fixture unavailable' }
$syntheticFixtureRoot = Join-Path $workspace 'tests/Ipc.Application.Verification'
New-Item -ItemType Directory -Path $syntheticFixtureRoot -Force | Out-Null
[IO.File]::WriteAllLines((Join-Path $syntheticFixtureRoot 'sportmaster.synthetic.json'), [string[]]$syntheticConfiguration)

if ($ParameterV3) {
 foreach ($schema in @('src/Ipc.Drivers.Hikrobot/parameters.json','src/Ipc.Integrations.SportmasterRcs/parameters.json')) {
  $target=Join-Path $workspace $schema
  New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
  $content=& git -C $repo show ($fixtureSource+':'+$schema)
  if($LASTEXITCODE -ne 0){throw 'Missing frozen parameter schema'}
  [IO.File]::WriteAllLines($target,[string[]]$content)
 }
}
$previousDesktop=$env:IPC_FROZEN_DESKTOP
$previousCleanMedia=$env:IPC_CLEAN_INSTALL_MEDIA
try {
 $env:IPC_FROZEN_DESKTOP=Join-Path $Media 'desktop/Ipc.Desktop.dll'
 if($CleanInstallBootstrap){$env:IPC_CLEAN_INSTALL_MEDIA=$Media}
 $mode=if($StabilitySeedUpgrade){'--stability-seed-upgrade'}elseif($InstallerFirstRunUi){'--installer-first-run-ui'}elseif($LifecycleUx){'--lifecycle-ux'}elseif($ParameterGuidanceStartup){'--parameter-guidance-startup'}elseif($DebugLogLive){'--debug-log-live'}elseif($QueryFilters){'--query-filters'}elseif($OperatorWorkspace){'--operator-workspace'}elseif($WorkstationStatusLogs){'--workstation-status-logs'}elseif($ReliabilityLogs){'--reliability-logs'}elseif($Win11Stability){'--win11-stability'}elseif($CleanInstallBootstrap){'--clean-install-bootstrap'}elseif($StartupExperience){'--startup-experience'}elseif($ParameterShellPolish){'--parameter-shell-polish'}elseif($TaskHierarchy){'--task-hierarchy'}elseif($ManagementMode){'--management-mode'}elseif($ParameterV3){'--parameter-v3'}elseif($AuditPerformance){'--audit-performance'}elseif($VisualProductization){'--visual-productization'}else{'--comm-native-pages'}
 $nativeArguments=@('run','--project',(Join-Path $Output 'NativeMedia.csproj'),'--',$workspace,$mode)
 if($Interactive){$nativeArguments += '--interactive'}
 & $Dotnet @nativeArguments *> (Join-Path $Output 'RUN.log')
} finally { $env:IPC_FROZEN_DESKTOP=$previousDesktop; $env:IPC_CLEAN_INSTALL_MEDIA=$previousCleanMedia }
$result=$LASTEXITCODE
foreach($relative in $inventory.Keys){if((Get-FileHash -LiteralPath (Join-Path $Media $relative)).Hash -ne $inventory[$relative]){throw "Verification changed medium: $relative"}}
if(Test-Path -LiteralPath (Join-Path $workspace '.artifacts/WINDOWS-WPF-EVIDENCE.txt')){
 $evidence=Get-Content -LiteralPath (Join-Path $workspace '.artifacts/WINDOWS-WPF-EVIDENCE.txt')
 Copy-Item -LiteralPath (Join-Path $evidence 'WINDOWS-WPF.log') -Destination $Output
 if($TaskHierarchy){Get-ChildItem -LiteralPath $evidence -Filter 'chain-*.json' | Copy-Item -Destination $Output}
 if($AuditPerformance -and (Test-Path -LiteralPath (Join-Path $evidence 'R02-performance.json'))){Copy-Item -LiteralPath (Join-Path $evidence 'R02-performance.json') -Destination $Output}
 if($StabilitySeedUpgrade -or $InstallerFirstRunUi -or $LifecycleUx -or $ParameterGuidanceStartup -or $DebugLogLive -or $WorkstationStatusLogs -or $OperatorWorkspace -or $QueryFilters -or $ReliabilityLogs -or $Win11Stability -or $ParameterV3 -or $ManagementMode -or $CleanInstallBootstrap -or $StartupExperience -or $ParameterShellPolish -or $TaskHierarchy){Get-ChildItem -LiteralPath $evidence -Filter '*.png' | Copy-Item -Destination $Output}
 elseif($VisualProductization){Get-ChildItem -LiteralPath $evidence -Filter 'after-*.png' | Copy-Item -Destination $Output}
 else { foreach($screen in @('commercial-history-1100x600.png','commercial-first-configuration.png')){
  if(Test-Path -LiteralPath (Join-Path $evidence $screen)){Copy-Item -LiteralPath (Join-Path $evidence $screen) -Destination $Output}
 }

 }
}
[ordered]@{Status=if($result -eq 0){'PASS'}else{'FAIL'};SourceCommit=$release.SourceCommit;FixtureSourceCommit=$fixtureSource;FixtureMatchesMedium=($fixtureSource -eq $release.SourceCommit);MediumInventorySha256=(Get-FileHash -LiteralPath (Join-Path $Media 'SHA256SUMS')).Hash;Scope='Native WPF pages with frozen published DLLs; synthetic identity/ports; no machine provisioning';ExitCode=$result;Binaries=@($refs|ForEach-Object{[ordered]@{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Output 'RESULT.json') -Encoding utf8NoBOM
if($result -ne 0){if(Test-Path -LiteralPath (Join-Path $Output 'WINDOWS-WPF.log')){Get-Content -LiteralPath (Join-Path $Output 'WINDOWS-WPF.log') -Tail 18};throw 'Frozen native WPF verification failed; original log retained'}
