param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
. $PSScriptRoot/project-environment.ps1
if (!(Test-Path variable:PSVersionTable) -or !$IsWindows) { throw 'Windows COM verification requires Windows' }
$shortcutRoot=(Resolve-Path "$PSScriptRoot/..").Path
$shortcutOutput=[IO.Path]::GetFullPath($Output)
if (![IO.Path]::IsPathRooted($Output) -or !($shortcutOutput.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase))) { throw 'Use a new absolute D: evidence directory' }
if (Test-Path -LiteralPath $shortcutOutput) { throw 'Evidence directory already exists' }
New-Item -ItemType Directory -Path $shortcutOutput | Out-Null
Push-Location $shortcutRoot
try {
    $shortcutPlatformHash=$null
    foreach ($shortcutCase in @(@{Name='en-US';ACP=1252},@{Name='zh-CN';ACP=936},@{Name='UTF-8';ACP=65001})) {
        $shortcutName=$shortcutCase.Name
        $shortcutManifest=Join-Path $shortcutOutput "$shortcutName.manifest"
        @"
<?xml version="1.0" encoding="utf-8"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="1.0.0.0" name="Ipc.Shortcut.CodePageProbe" type="win32"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings>
    <activeCodePage xmlns="http://schemas.microsoft.com/SMI/2019/WindowsSettings">$shortcutName</activeCodePage>
  </windowsSettings></application>
</assembly>
"@ | Set-Content -LiteralPath $shortcutManifest -Encoding utf8
        # Scope the manifest to the probe project; a global ApplicationManifest
        # also embeds it in referenced product DLLs and changes their hashes.
        dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true "-p:ShortcutProbeManifest=$shortcutManifest" *> (Join-Path $shortcutOutput "$shortcutName-build.log")
        if ($LASTEXITCODE) { throw "Probe build failed: $shortcutName" }
        # The apphost manifest selects the child ACP; dotnet.exe would ignore it.
        & ./tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.exe . --installer-options --expected-acp $shortcutCase.ACP *> (Join-Path $shortcutOutput "$shortcutName.log")
        if ($LASTEXITCODE) { Get-Content (Join-Path $shortcutOutput "$shortcutName.log"); throw "Shortcut boundary failed: $shortcutName" }
        & ./tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.exe . --shortcut-lifecycle --expected-acp $shortcutCase.ACP *> (Join-Path $shortcutOutput "$shortcutName-lifecycle.log")
        if ($LASTEXITCODE) { Get-Content (Join-Path $shortcutOutput "$shortcutName-lifecycle.log"); throw "Shortcut lifecycle failed: $shortcutName" }
        $shortcutProbe='tests/Ipc.Application.Verification/bin/Release/net10.0'
        $shortcutCurrentHash=(Get-FileHash -LiteralPath "$shortcutProbe/Ipc.Platform.dll" -Algorithm SHA256).Hash
        if ($shortcutPlatformHash -and $shortcutCurrentHash -ne $shortcutPlatformHash) { throw "Product DLL changed across process code pages: $shortcutName" }
        $shortcutPlatformHash=$shortcutCurrentHash
        $shortcutLog=Get-Content -LiteralPath (Join-Path $shortcutOutput "$shortcutName-lifecycle.log") -Raw
        $shortcutActual=[regex]::Match($shortcutLog,'PASS actual process ACP=(\d+)')
        if (!$shortcutActual.Success -or [int]$shortcutActual.Groups[1].Value -ne $shortcutCase.ACP) { throw "Missing actual ACP proof: $shortcutName" }
        [ordered]@{
            SourceCommit=(& git rev-parse HEAD); GitStatus=@(& git status --porcelain)
            ExpectedACP=$shortcutCase.ACP; ActualACP=[int]$shortcutActual.Groups[1].Value
            OS=[Environment]::OSVersion.VersionString; Sdk=(& dotnet --version)
            ManifestSha256=(Get-FileHash -LiteralPath $shortcutManifest -Algorithm SHA256).Hash
            ApphostSha256=(Get-FileHash -LiteralPath "$shortcutProbe/Ipc.Application.Verification.exe" -Algorithm SHA256).Hash
            VerificationDllSha256=(Get-FileHash -LiteralPath "$shortcutProbe/Ipc.Application.Verification.dll" -Algorithm SHA256).Hash
            PlatformDllSha256=$shortcutCurrentHash
            InstallerLogSha256=(Get-FileHash -LiteralPath (Join-Path $shortcutOutput "$shortcutName.log") -Algorithm SHA256).Hash
            LifecycleLogSha256=(Get-FileHash -LiteralPath (Join-Path $shortcutOutput "$shortcutName-lifecycle.log") -Algorithm SHA256).Hash
            Status='PASS'; Scope='Isolated real COM; no machine installation or policy registration'
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $shortcutOutput "$shortcutName-result.json") -Encoding utf8
        Write-Output "PASS Unicode COM create/read/upgrade/ownership/remove ACP=$($shortcutCase.ACP)"
    }
}
finally { Pop-Location }
