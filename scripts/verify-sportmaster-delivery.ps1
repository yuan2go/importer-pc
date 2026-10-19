#requires -Version 7.0
param([Parameter(Mandatory)][string]$Release,[Parameter(Mandatory)][string]$Output,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Native frozen-media journeys require Windows'}
$Release=(Resolve-Path -LiteralPath $Release).Path
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Choose a new evidence directory; original failures are retained'}
New-Item -ItemType Directory -Path $Output | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$expected=(Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw|ConvertFrom-Json).sdk.version
if((& $Dotnet --version) -ne $expected){throw 'Use the pinned SDK'}
& python (Join-Path $PSScriptRoot 'check-commercial-delivery.py') --release $Release --evidence (Join-Path $Output 'acceptance.json') --prepare
if($LASTEXITCODE -ne 0){throw 'Media verification failed'}
$record=Get-Content -LiteralPath (Join-Path $Release 'RELEASE.json') -Raw|ConvertFrom-Json
$package=$record.Packages|Where-Object Target -eq 'win-x64'
$media=Join-Path $Release ([IO.Path]::GetFileNameWithoutExtension($package.File))
$plan=Get-Content -LiteralPath (Join-Path $Output 'acceptance.json') -Raw|ConvertFrom-Json
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]::new($identity)
[ordered]@{SourceCommit=$record.SourceCommit;OS=[Environment]::OSVersion.VersionString;ProcessArchitecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString();Elevated=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);Sdk=$expected;WindowsLifecycle='NOT_RUN: no disposable admin/reboot/two-account environment provisioned';Equipment='BLOCKED: no real SDK/contract/equipment qualification';Scope='Non-destructive native frozen DLL verification; synthetic runtime identity and ports'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'environment.json') -Encoding utf8NoBOM
function Evidence($directory){
 @((Get-ChildItem -LiteralPath $directory -File | Where-Object {$_.Extension -in '.json','.log','.png'} | Sort-Object @{Expression={if($_.Name -eq 'RESULT.json'){0}else{1}}},FullName) | ForEach-Object {
  [ordered]@{Path=[IO.Path]::GetRelativePath($Output,$_.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
 })
}
$mediaResult=Join-Path $Output 'media.json'
[ordered]@{Status='PASS';SourceCommit=$record.SourceCommit;MediumInventorySha256=$plan.MediumInventorySha256;Scope='Actual unified release verification: both ZIPs, inventories, source, composition and shared DLLs'}|ConvertTo-Json|Set-Content -LiteralPath $mediaResult -Encoding utf8NoBOM
$row=$plan.Cases|Where-Object Id -eq 'media';$row.Status='PASS';$row.Evidence=@([ordered]@{Path='media.json';Sha256=(Get-FileHash -LiteralPath $mediaResult).Hash})
$failed=$false
foreach($case in @(@('core','verify-commercial-media.ps1'),@('wpf','verify-commercial-wpf.ps1'),@('removal','verify-commercial-removal.ps1'))){
 $directory=Join-Path $Output $case[0]
 $row=$plan.Cases|Where-Object Id -eq $case[0]
 try {
  & (Join-Path $PSScriptRoot $case[1]) -Media $media -Output $directory -Dotnet $Dotnet
  $result=Get-Content -LiteralPath (Join-Path $directory 'RESULT.json') -Raw|ConvertFrom-Json
  if($result.Status -ne 'PASS'){throw 'Frozen journey did not pass'}
  $row.Status='PASS';$row.Reason=$result.Scope;$row.Evidence=@(Evidence $directory)
 } catch {$failed=$true;$row.Status='FAIL';$row.Reason=$_.Exception.Message;$row.Evidence=if(Test-Path -LiteralPath $directory){@(Evidence $directory)}else{@()}}
 # Retain each completed/failed result immediately; another journey may still run.
 $plan|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $Output 'acceptance.json') -Encoding utf8NoBOM
}
& python (Join-Path $PSScriptRoot 'check-commercial-delivery.py') --release $Release --evidence (Join-Path $Output 'acceptance.json') --level engineering *> (Join-Path $Output 'engineering-gate.json')
$engineeringExit=$LASTEXITCODE
$engineering=Get-Content -LiteralPath (Join-Path $Output 'engineering-gate.json') -Raw -Encoding utf8|ConvertFrom-Json
if($engineeringExit -ne 0 -or $failed -or $engineering.Status -ne 'PASS' -or $engineering.Level -ne 'engineering' -or $engineering.SourceCommit -ne $record.SourceCommit -or $engineering.MediumInventorySha256 -ne $plan.MediumInventorySha256){throw 'Engineering candidate rejected; failures and NOT_RUN/BLOCKED retained'}
& python (Join-Path $PSScriptRoot 'check-commercial-delivery.py') --release $Release --evidence (Join-Path $Output 'acceptance.json') --level production *> (Join-Path $Output 'production-gate.json')
$productionExit=$LASTEXITCODE
$production=Get-Content -LiteralPath (Join-Path $Output 'production-gate.json') -Raw -Encoding utf8|ConvertFrom-Json
if($productionExit -ne 1 -or $production.Status -ne 'FAIL' -or $production.Level -ne 'production' -or $production.SourceCommit -ne $record.SourceCommit -or $production.MediumInventorySha256 -ne $plan.MediumInventorySha256 -or @($production.Outstanding).Count -lt 1){throw 'Production refusal was not a valid matching policy decision; a crash/encoding error cannot count as expected rejection'}
Write-Output 'Engineering journeys PASS; production rejected with individual NOT_RUN/BLOCKED. Follow the shipped lifecycle/support runbook in an authorized disposable environment.'
