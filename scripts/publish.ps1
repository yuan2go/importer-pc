param(
    [ValidateSet('Simulation','Production')][string]$Mode = 'Simulation',
    [string]$Output = '.artifacts/win-x64',
    [string]$ProductDefinition = 'config/product.reference.json',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" } }
# Pin a distinct RID lock instead of silently rewriting the portable dependency lock.
$lock = 'packages.win-x64.lock.json'
if (-not $NoRestore) {
    foreach ($project in @('Ipc.Desktop','Ipc.Product','Ipc.Fixture')) {
        Invoke-Dotnet restore "src/$project/$project.csproj" -r win-x64 "-p:NuGetLockFilePath=$lock" --locked-mode
    }
}
if (Test-Path $Output) { throw 'Publication requires a new output directory; prior release retained' }
foreach ($project in @('Ipc.Desktop','Ipc.Product')) {
    Invoke-Dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true --no-restore "-p:NuGetLockFilePath=$lock" '-p:EnableWindowsTargeting=true' '-m:1' -o "$Output/app"
}
# Production is deliberately NotReady and does not ship the fixture executable.
if ($Mode -eq 'Simulation') {
    Invoke-Dotnet publish src/Ipc.Fixture/Ipc.Fixture.csproj -c Release -r win-x64 --self-contained true --no-restore "-p:NuGetLockFilePath=$lock" '-m:1' -o "$Output/fixture"
}
& python scripts/seal.py --bin "$Output/app" --mode $Mode --architecture x64 --product-definition $ProductDefinition
if ($LASTEXITCODE -ne 0) { throw 'Manifest seal failed' }
Copy-Item scripts/station.ps1 "$Output/station.ps1"
Copy-Item docs/runtime/operations.md "$Output/OPERATIONS.md"
Get-ChildItem $Output -Recurse -Filter '*.lock.json' | Out-Null
Copy-Item global.json "$Output/global.json"
New-Item -ItemType Directory "$Output/dependencies" | Out-Null
Get-ChildItem src -Recurse -Filter 'packages*.lock.json' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object { Copy-Item $_.FullName "$Output/dependencies/$($_.Directory.Name)-$($_.Name)" }
$absolute = (Resolve-Path $Output).Path
$checksumLines = Get-ChildItem $absolute -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($absolute.Length + 1).Replace('\','/')
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $relative"
}
$checksumLines | Set-Content "$Output/SHA256SUMS" -Encoding utf8
Compress-Archive -Path "$Output/*" -DestinationPath "$Output.zip"
Write-Output "Published $Mode win-x64: $Output.zip (desktop runtime validation required on Windows)"
