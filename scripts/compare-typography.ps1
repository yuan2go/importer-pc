#requires -Version 7.0
param([string]$Baseline='a43ecb86efdc78ddc48e7082dcb6f96848138a8b', [string]$Output)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'project-environment.ps1')
if(-not $IsWindows){throw 'Native Windows WPF required'}
if($Baseline -notmatch '^[a-f0-9]{40}$'){throw 'Full baseline commit required'}
if(-not $Output){$Output=Join-Path $repo ('.artifacts/typography-comparison-'+[guid]::NewGuid().ToString('N'))}
$Output=[IO.Path]::GetFullPath($Output)
if(-not $Output.StartsWith('D:\',[StringComparison]::OrdinalIgnoreCase)){throw 'Project evidence must stay on D:'}
if(Test-Path -LiteralPath $Output){throw 'Use a new evidence directory'}
New-Item -ItemType Directory -Path $Output | Out-Null
$source=Join-Path $Output 'baseline-source'
$archive=Join-Path $Output 'baseline.zip'
& git -C $repo archive --format=zip "--output=$archive" $Baseline
if($LASTEXITCODE -ne 0){throw 'Baseline archive failed'}
Expand-Archive -LiteralPath $archive -DestinationPath $source
$fixture='tests/Ipc.Desktop.UiVerification/TypographyComparison.cs'
Copy-Item -LiteralPath (Join-Path $repo $fixture) -Destination (Join-Path $source $fixture)
$entry=Join-Path $source 'tests/Ipc.Desktop.UiVerification/Program.cs'
$original=[IO.File]::ReadAllText($entry)
$needle='if (args.Contains("--task-hierarchy-chain"))'
if(-not $original.Contains($needle)){throw 'Baseline dispatcher changed; inspect before adapting'}
[IO.File]::WriteAllText($entry,$original.Replace($needle,'if (args.Contains("--typography-comparison")) await VerifyTypographyComparison(root, evidence, lines, args);'+"`n                else "+$needle))
$previousFixture=$env:IPC_TYPOGRAPHY_FIXTURE
try {
 $env:IPC_TYPOGRAPHY_FIXTURE=Join-Path $Output 'private-fixture.json'
 Push-Location $repo
 try {
  & dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false *> (Join-Path $Output 'build.log')
  if($LASTEXITCODE -ne 0){throw 'Current build failed'}
  & dotnet build (Join-Path $source 'tests/Ipc.Desktop.UiVerification') -c Release -p:RestoreLockedMode=true "-p:SourceRevisionId=$Baseline" -m:1 -nr:false *> (Join-Path $Output 'baseline-build.log')
  if($LASTEXITCODE -ne 0){throw 'Baseline build failed'}
  foreach($variant in @('baseline','system','noto')) {
   $binary='tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll'
   $arguments=@($repo,'--typography-comparison')
   if($variant -eq 'baseline'){$binary=Join-Path $source $binary;$arguments+='--baseline'}
   if($variant -eq 'system'){$arguments+='--system-family'}
   & dotnet $binary @arguments
   $result=$LASTEXITCODE
   Copy-Item -LiteralPath (Join-Path $repo '.artifacts/WINDOWS-WPF-EVIDENCE.txt') -Destination (Join-Path $Output ($variant+'.txt'))
   if($result -ne 0){throw "$variant failed; original evidence pointer retained"}
  }
  [ordered]@{BaselineProduction=$Baseline;CandidateSource=(& git rev-parse HEAD);FixtureSha256=(Get-FileHash -LiteralPath (Join-Path $Output 'private-fixture.json')).Hash;
   BaselineAdaptation='Only comparison fixture and dispatcher added; original src/ untouched';Scope='Native synthetic typography comparison; no field/operator acceptance'} |
   ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'SOURCE.json') -Encoding utf8NoBOM
 } finally {Pop-Location}
} finally {$env:IPC_TYPOGRAPHY_FIXTURE=$previousFixture}
Write-Output "Evidence pointers: $Output; keep private-fixture.json outside Git"
