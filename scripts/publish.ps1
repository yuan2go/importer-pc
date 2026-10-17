param(
    [ValidateSet('Simulation','Production')][string]$Mode='Simulation',
    [string]$Output='.artifacts/win-x64',
    [string]$ProductDefinition='config/product.reference.json',
    [ValidateSet('core-reference','barcode-reference')][string]$Composition='core-reference',
    [string]$ExtensionKey,
    [string]$ExtensionTrust,
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
Set-Location (Join-Path $PSScriptRoot '..')
# One packaging authority for dependency closure, exact inventory and provenance.
$arguments=@('scripts/package-reference.py','--target','win-x64','--mode',$Mode,'--output',$Output,'--composition',$Composition,'--product-definition',$ProductDefinition)
if ($ExtensionKey) {$arguments+=@('--extension-key',$ExtensionKey)}
if ($ExtensionTrust) {$arguments+=@('--extension-trust',$ExtensionTrust)}
if ($NoRestore) {$arguments+='--no-restore'}
& python3 @arguments
if ($LASTEXITCODE -ne 0) {throw "Publication failed ($LASTEXITCODE); retain original output/evidence and select a new output after inspection"}
