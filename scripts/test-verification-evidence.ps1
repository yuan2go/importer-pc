#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'project-environment.ps1')
. (Join-Path $PSScriptRoot 'verification-evidence.ps1')
$fixture=[IO.Path]::GetFullPath(('D:/DevCache/importer-pc/temp/cr-evidence-'+[guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $fixture | Out-Null
function Reject([scriptblock]$Action,[string]$Reason){$failed=$false;try{& $Action}catch{$failed=$true};if(!$failed){throw $Reason};"PASS $Reason"}
try {
 $source=Join-Path $fixture 'source'
 foreach($relative in @('tests/Ipc.Application.Verification/bin/Release/net10.0','tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows','src/Ipc.Desktop/bin/Release/net10.0-windows')){
  $directory=Join-Path $source $relative;New-Item -ItemType Directory -Path $directory -Force | Out-Null
  [IO.File]::WriteAllText((Join-Path $directory 'fixture.dll'),'frozen build bytes')
 }
 $bundle=Join-Path $fixture 'frozen';Save-VerificationBundle $source $bundle;Assert-VerificationBundle $bundle
 [IO.File]::WriteAllText((Join-Path $source 'src/Ipc.Desktop/bin/Release/net10.0-windows/fixture.dll'),'later same-source rebuild')
 Assert-VerificationBundle $bundle
 $published=Join-Path $fixture 'published';Copy-Item -LiteralPath (Join-Path $bundle 'desktop') -Destination $published -Recurse
 Assert-PublishedBundle $published $bundle
 [IO.File]::WriteAllText((Join-Path $published 'fixture.dll'),'different executable bytes')
 Reject {Assert-PublishedBundle $published $bundle} 'publish cannot use later build bytes'
 [IO.File]::WriteAllText((Join-Path $bundle 'native/fixture.dll'),'changed native fixture')
 Reject {Assert-VerificationBundle $bundle} 'native cannot use changed frozen bytes'
 $media=Join-Path $fixture 'media';New-Item -ItemType Directory -Path $media | Out-Null
 [IO.File]::WriteAllText((Join-Path $media 'app.dll'),'reviewed')
 $inventory=@{'app.dll'=(Get-FileHash -LiteralPath (Join-Path $media 'app.dll')).Hash};Assert-MediumInventory $media $inventory
 [IO.File]::WriteAllText((Join-Path $media 'unlisted.dll'),'must not execute')
 Reject {Assert-MediumInventory $media $inventory} 'unlisted executable input invalidates medium evidence'
 New-Item -ItemType Directory -Path (Join-Path $source '.artifacts') | Out-Null
 [IO.File]::WriteAllText((Join-Path $source '.artifacts/WINDOWS-WPF-EVIDENCE.txt'),'stale result')
 Reset-NativeEvidence $source
 Reject {Read-NativeEvidence $source} 'failed native invocation cannot inherit a stale evidence pointer'
 $tokens=$null;$errors=$null
 $syntax=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'application.ps1'),[ref]$tokens,[ref]$errors)
 $quote=$syntax.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Quote-WindowsArgument'},$true)
 . ([scriptblock]::Create($quote.Extent.Text))
 $child=Join-Path $fixture 'argument echo.ps1';[IO.File]::WriteAllText($child,'[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);ConvertTo-Json -InputObject @($args) -Compress')
 $arguments=@('D:\data with spaces\','D:\configuration\','D:\unicode-中文\','a"quote','two\\slashes\')
 $start=[Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $start.Arguments=(@('-NoLogo','-NoProfile','-File',$child)+$arguments | ForEach-Object {Quote-WindowsArgument $_}) -join ' '
 $process=[Diagnostics.Process]::Start($start)
 try {
  $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
  if(!$process.WaitForExit(10000)){throw 'Argument echo timed out'}
  $actual=@($stdout.GetAwaiter().GetResult() | ConvertFrom-Json)
  if($process.ExitCode -ne 0 -or $actual.Count -ne $arguments.Count){throw ('Argument echo failed: '+$stderr.GetAwaiter().GetResult())}
  for($i=0;$i -lt $actual.Count;$i++){if($actual[$i] -cne $arguments[$i]){throw ('Windows argument round trip changed operand '+$i+': '+($actual[$i]|ConvertTo-Json -Compress)+' expected '+($arguments[$i]|ConvertTo-Json -Compress))}}
  'PASS actual Windows command-line round trip preserves trailing slashes, spaces, quotes and Unicode'
 } finally {try{if(!$process.HasExited){$process.Kill($true);$process.WaitForExit(10000)|Out-Null}}finally{$process.Dispose()}}
} finally {
 $cleanup=[IO.Path]::GetFullPath($fixture)
 if(!$cleanup.StartsWith('D:\DevCache\importer-pc\temp\cr-evidence-',[StringComparison]::OrdinalIgnoreCase)){throw 'Refuse cleanup outside this generated fixture'}
 Remove-Item -LiteralPath $cleanup -Recurse -Force
}
