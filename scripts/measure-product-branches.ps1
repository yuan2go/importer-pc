#requires -Version 7.0
param([Parameter(Mandatory)][string]$Output, [ValidateSet('Debug','Release')][string]$Configuration='Debug', [switch]$IncludeWpf)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'project-environment.ps1')
$run=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $run){throw 'Use a fresh evidence directory; sessions are never mixed or overwritten'}
New-Item -ItemType Directory -Path $run | Out-Null
$tool=Join-Path $env:DOTNET_CLI_HOME 'tools/coverlet-10.1.0'
if(!(Test-Path -LiteralPath (Join-Path $tool 'coverlet.exe'))){
 dotnet tool install coverlet.console --version 10.1.0 --tool-path $tool
 if($LASTEXITCODE){throw 'Pinned branch collector installation failed'}
}
$collector=Join-Path $tool 'coverlet.exe'
$child=Join-Path $run 'binaries/application'
$core=Join-Path $run 'binaries/core'
foreach($item in @(@("tests/Ipc.Application.Verification/bin/$Configuration/net10.0",$child),@("tests/Ipc.Core.Tests/bin/$Configuration/net10.0-windows",$core))){
 $source=Join-Path $repo $item[0]
 if(!(Test-Path -LiteralPath $source)){throw "Build first: $source"}
 New-Item -ItemType Directory -Path $item[1] -Force | Out-Null
 Copy-Item -Path (Join-Path $source '*') -Destination $item[1] -Recurse
}
$env:IPC_REPO_ROOT=$repo
$env:IPC_VERIFICATION_EXECUTABLE=Join-Path $child 'Ipc.Application.Verification.dll'
$env:IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME='1'
$env:IPC_FROZEN_DESKTOP=Join-Path $repo "src/Ipc.Desktop/bin/$Configuration/net10.0-windows/Ipc.Desktop.dll"
$executions=[Collections.Generic.List[object]]::new()
function Get-BranchSourceInputs {
 $items=@(Get-ChildItem -LiteralPath (Join-Path $repo 'src'),(Join-Path $repo 'config') -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.json','.xaml','.csproj','.props','.targets') } | Sort-Object FullName | ForEach-Object { [ordered]@{path=$_.FullName.Substring($repo.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
 return ($items | ConvertTo-Json -Compress)
}
$originalInputs=Get-BranchSourceInputs
$originalInputs | Set-Content -LiteralPath (Join-Path $run 'source-inputs.json') -Encoding utf8
$sourceIdentity=[ordered]@{commit=(& git -C $repo rev-parse HEAD); diffSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((& git -C $repo diff HEAD | Out-String))))); configuration=$Configuration; collector='coverlet.console 10.1.0'; startedUtc=[DateTimeOffset]::UtcNow.ToString('o')}
function Invoke-BranchMeasurement([string]$name,[string]$directory,[string]$target,[string]$arguments){
 $report=Join-Path $run $name
 $started=[DateTimeOffset]::UtcNow
 $modules=@(Get-ChildItem -LiteralPath $directory -Filter 'Ipc.*.dll' | ForEach-Object { [ordered]@{name=$_.BaseName; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; pdbSha256=$(if(Test-Path -LiteralPath ([IO.Path]::ChangeExtension($_.FullName,'.pdb'))){(Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($_.FullName,'.pdb')) -Algorithm SHA256).Hash}else{$null})} })
 $modules | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run "$name.modules.json") -Encoding utf8
 & $collector $directory --target $target --targetargs $arguments --include '[Ipc.*]*' --exclude '[Ipc.*.Verification]*' --exclude '[Ipc.Core.Tests]*' --format opencover --format json --output $report *> (Join-Path $run "$name.log")
 $code=$LASTEXITCODE
 $executions.Add([ordered]@{name=$name; target=$target; arguments=$arguments; exitCode=$code; startedUtc=$started.ToString('o'); endedUtc=[DateTimeOffset]::UtcNow.ToString('o')})
 $executions | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'executions.json') -Encoding utf8
 return $code
}
try {
 $sourceIdentity | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source.json') -Encoding utf8
 $coreArgs='test "'+(Join-Path $core 'Ipc.Core.Tests.dll')+'" --logger "trx;LogFileName=core.trx" --results-directory "'+(Join-Path $run 'tests')+'"'
 $failed=Invoke-BranchMeasurement 'core' $child (Get-Command dotnet).Source $coreArgs
 if($IncludeWpf){
  $ui=Join-Path $run 'binaries/wpf'; New-Item -ItemType Directory -Path $ui | Out-Null
  Copy-Item -Path (Join-Path $repo "tests/Ipc.Desktop.UiVerification/bin/$Configuration/net10.0-windows/*") -Destination $ui -Recurse
  # These are actual production assemblies, not linked test sources. Startup
  # fixtures load the isolated production copy while the collector owns its bytes.
  foreach($name in @('Ipc.Desktop','Ipc.Assembly')){
   foreach($extension in @('.dll','.pdb')){ Copy-Item -LiteralPath (Join-Path $repo "src/Ipc.Desktop/bin/$Configuration/net10.0-windows/$name$extension") -Destination $ui }
  }
  $env:IPC_FROZEN_DESKTOP=Join-Path $ui 'Ipc.Desktop.dll'
  foreach($suite in @('startup-defaults-ui','window-preference','first-preference-save','parameter-guidance-startup','startup-read-failure','station-settings')){
   $code=Invoke-BranchMeasurement "wpf-$suite" $ui (Get-Command dotnet).Source ('"'+(Join-Path $ui 'Ipc.Desktop.UiVerification.dll')+'" "'+$repo+'" --'+$suite)
   if($code){$failed=1}
   # Preserve native assertions, actual DPI and captures, not just collector exits.
   $pointer=Join-Path $repo '.artifacts/WINDOWS-WPF-EVIDENCE.txt'
   $evidence=if(Test-Path -LiteralPath $pointer){(Get-Content -LiteralPath $pointer -Raw).Trim()}else{$null}
   $nativeLog=if($evidence){Join-Path $evidence 'WINDOWS-WPF.log'}else{$null}
   if(!$nativeLog -or !(Test-Path -LiteralPath $nativeLog) -or (Get-Item -LiteralPath $nativeLog).LastWriteTimeUtc -lt [DateTimeOffset]::Parse($executions[-1].startedUtc).UtcDateTime){
    $failed=1; "Missing current native evidence: $suite" | Add-Content -LiteralPath (Join-Path $run "wpf-$suite.log")
   }else{
    $native=Join-Path $run "windows/$suite"; New-Item -ItemType Directory -Path $native -Force | Out-Null
    Copy-Item -LiteralPath $nativeLog -Destination $native
    Get-ChildItem -LiteralPath $evidence -File -Filter '*.png' | Copy-Item -Destination $native
   }
  }
 }
 $sourceIdentity['endCommit']=(& git -C $repo rev-parse HEAD)
 $sourceIdentity['sourceInputsChanged']=($originalInputs -cne (Get-BranchSourceInputs))
 $sourceIdentity | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source.json') -Encoding utf8
 python (Join-Path $PSScriptRoot 'report-product-branches.py') --root $repo --results $run
 if($LASTEXITCODE){throw 'Actual branch report validation failed'}
 if($sourceIdentity.sourceInputsChanged){throw 'Production inputs changed during measurement; keep raw evidence but rerun a frozen checkout'}
 if($failed){throw 'Regression failed; branch evidence retained but is not a passing qualification'}
} finally {
 Remove-Item Env:IPC_VERIFICATION_EXECUTABLE -ErrorAction SilentlyContinue
}
