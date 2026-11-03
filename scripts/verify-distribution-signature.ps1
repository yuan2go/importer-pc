#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Media,
    [Parameter(Mandatory)][string[]]$ApprovedSigner,
    [Parameter(Mandatory)][string]$Installer,
    [string]$SignTool = 'signtool.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-OrdinaryPath([string]$Path) {
    $current = Get-Item -LiteralPath ([IO.Path]::GetFullPath($Path)) -Force
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked distribution paths are refused' }
        $current = if ($current -is [IO.FileInfo]) { $current.Directory } else { $current.Parent }
    }
}

function Assert-CmsPolicy([byte[]]$Encoded, [Collections.Generic.HashSet[string]]$Approved, [int]$Depth = 0, [bool]$Timestamp = $false) {
    if ($Depth -gt 4) { throw 'Nested signature depth exceeds policy' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode($Encoded)
    if ($cms.SignerInfos.Count -lt 1 -or $cms.SignerInfos.Count -gt 16) { throw 'Invalid signature count' }
    $strongDigests = @('2.16.840.1.101.3.4.2.1', '2.16.840.1.101.3.4.2.2', '2.16.840.1.101.3.4.2.3')
    if ($Timestamp) {
        if ($cms.ContentInfo.ContentType.Value -ne '1.2.840.113549.1.9.16.1.4') { throw 'Timestamp content must be RFC3161 TSTInfo' }
        $info = $null; $consumed = 0
        if (-not [Security.Cryptography.Pkcs.Rfc3161TimestampTokenInfo]::TryDecode([ReadOnlyMemory[byte]]::new($cms.ContentInfo.Content), [ref]$info, [ref]$consumed) -or
            $consumed -ne $cms.ContentInfo.Content.Length -or $info.HashAlgorithmId.Value -notin $strongDigests) { throw 'Timestamp message imprint must use SHA256 or stronger' }
        $expectedHashBytes = switch ($info.HashAlgorithmId.Value) {
            '2.16.840.1.101.3.4.2.1' { 32 }
            '2.16.840.1.101.3.4.2.2' { 48 }
            '2.16.840.1.101.3.4.2.3' { 64 }
        }
        if ($info.GetMessageHash().Length -ne $expectedHashBytes) { throw 'Invalid timestamp message imprint size' }
    }
    foreach ($signer in $cms.SignerInfos) {
        if (($Timestamp -and $signer.DigestAlgorithm.Value -notin $strongDigests) -or
            ((-not $Timestamp) -and $signer.DigestAlgorithm.Value -ne '2.16.840.1.101.3.4.2.1')) { throw 'Product signature must use SHA256; timestamp signer digest must use SHA256 or stronger' }
        if (-not $Timestamp -and ($null -eq $signer.Certificate -or -not $Approved.Contains($signer.Certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)))) { throw 'Embedded signature has an unapproved signer' }
        foreach ($counter in $signer.CounterSignerInfos) {
            if ($counter.DigestAlgorithm.Value -notin $strongDigests) { throw 'Legacy timestamp does not meet minimum SHA256 policy' }
        }
        foreach ($attribute in $signer.UnsignedAttributes) {
            $timestampAttribute = $attribute.Oid.Value -in '1.2.840.113549.1.9.16.2.14', '1.3.6.1.4.1.311.3.3.1'
            if ($attribute.Oid.Value -eq '1.3.6.1.4.1.311.2.4.1' -or $timestampAttribute) {
                foreach ($value in $attribute.Values) {
                    Assert-CmsPolicy $value.RawData $Approved ($Depth + 1) ($Timestamp -or $timestampAttribute)
                }
            }
        }
    }
}

function Assert-PeSignatureDigests([IO.Stream]$Stream, [Collections.Generic.HashSet[string]]$Approved) {
    $Stream.Position = 0
    $pe = [Reflection.PortableExecutable.PEReader]::new($Stream, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
    $reader = [IO.BinaryReader]::new($Stream, [Text.Encoding]::UTF8, $true)
    try {
        $table = $pe.PEHeaders.PEHeader.CertificateTableDirectory
        $position = [long]$table.RelativeVirtualAddress # Certificate table uses a file offset, not an image RVA.
        $end = $position + [long]$table.Size
        if ($position -lt 64 -or $table.Size -lt 8 -or $table.Size -gt 16777216 -or $end -gt $Stream.Length) { throw 'Invalid certificate table bounds' }
        $count = 0
        while ($position -lt $end) {
            if (++$count -gt 16 -or $end - $position -lt 8) { throw 'Invalid certificate table entry count' }
            $Stream.Position = $position
            $length = [long]$reader.ReadUInt32()
            if ($length -lt 8 -or $length -gt $end - $position -or $reader.ReadUInt16() -ne 0x200 -or $reader.ReadUInt16() -ne 2) { throw 'Unsupported certificate table entry' }
            $encoded = $reader.ReadBytes([int]($length - 8))
            if ($encoded.Length -ne $length - 8) { throw 'Truncated embedded signature' }
            Assert-CmsPolicy $encoded $Approved
            $position += ($length + [long]7) -band ([long]-8)
            if ($position -gt $end) { throw 'Invalid certificate entry alignment' }
        }
    } finally { $reader.Dispose(); $pe.Dispose() }
}

$certificates = [Collections.Generic.List[Security.Cryptography.X509Certificates.X509Certificate2]]::new()
try {
    if (-not $IsWindows) { throw 'Authenticode verification requires Windows' }
    Add-Type -AssemblyName System.Reflection.Metadata
    Add-Type -AssemblyName System.Security.Cryptography.Pkcs
    $tool = (Get-Command -Name $SignTool -CommandType Application -ErrorAction Stop).Source
    Assert-OrdinaryPath $tool
    $approved = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $ApprovedSigner) {
        Assert-OrdinaryPath $path
        $file = Get-Item -LiteralPath $path
        if ($file.Extension -ne '.cer' -or $file.Length -gt 1048576) { throw 'Provide externally approved public .cer certificates; no private keys' }
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($file.FullName)
        $certificates.Add($certificate)
        if ($certificate.HasPrivateKey) { throw 'Approved signer input must not contain a private key' }
        $codeSigning = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
            ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })
        if ($codeSigning.Count -eq 0) { throw 'Approved certificate lacks explicit code-signing usage' }
        [void]$approved.Add($certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256))
    }
    if ($approved.Count -eq 0) { throw 'No approved public signer supplied' }
    Assert-OrdinaryPath $Media
    $root = (Get-Item -LiteralPath $Media).FullName
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked distribution entries are refused' }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            elseif ($item.Name -like 'Ipc.*' -and $item.Extension -in '.exe', '.dll') { $files.Add($item) }
        }
    }
    if ($files.Count -lt 4) { throw 'Missing complete product PE inventory' }
    foreach ($required in @('app/Ipc.Assembly.exe', 'app/Ipc.Platform.dll', 'desktop/Ipc.Desktop.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "Missing product entry: $required" }
    }
    Assert-OrdinaryPath $Installer
    $outer = Get-Item -LiteralPath $Installer
    if ($outer -isnot [IO.FileInfo] -or $outer.Extension -ne '.exe') { throw 'Provide the actual final signed installer EXE' }
    $files.Add($outer)
    $signatures = @(foreach ($file in $files | Sort-Object FullName -Unique) {
        # Keep the actual file object open against writes/replacement throughout
        # Windows policy evaluation, SignTool and final-byte hashing.
        $locked = [IO.FileStream]::new($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
            if ($signature.Status -ne 'Valid' -or $signature.SignatureType.ToString() -ne 'Authenticode' -or $null -eq $signature.SignerCertificate) { throw "Invalid embedded signature: $($file.Name)" }
            $signer = $signature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
            if (-not $approved.Contains($signer)) { throw "Unapproved signer: $($file.Name)" }
            if ($null -eq $signature.TimeStamperCertificate) { throw "Missing timestamp: $($file.Name)" }
            Assert-PeSignatureDigests $locked $approved
            $verification = @(& $tool verify /pa /all /v /tw $file.FullName 2>&1)
            if ($LASTEXITCODE -ne 0) { throw "SignTool rejected signature(s) or emitted warnings: $($file.Name)" }
            $locked.Position = 0
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($locked))
            [ordered]@{
                File = if ($file.FullName -eq $outer.FullName) { 'FINAL-INSTALLER/' + $file.Name } else { [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/') }
                Bytes = $locked.Length; Sha256 = $hash; SignerCertificateSha256 = $signer
                TimestampCertificateSha256 = $signature.TimeStamperCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
                Status = 'Valid'; DigestPolicy = 'SHA256'; TimestampDigestPolicy = 'SHA256/384/512 signer and message imprint'; AllSignatures = 'SignTool /pa /all /tw exit 0'
            }
        } finally { $locked.Dispose() }
    })
    [ordered]@{
        Status = 'PASS'; Scope = 'RecursiveProductPeAndFinalInstaller'; InstallerVerified = $true
        Files = $signatures; VerifiedAt = [DateTimeOffset]::UtcNow.ToString('O')
        RevocationAssessment = 'Windows trust policy evaluated; independent online/offline revocation matrix NOT_RUN'
        PayloadSourceBinding = 'NOT_VERIFIED_BY_SIGNATURE_TOOL'; SmartScreenReputation = 'NOT_RUN'
    } | ConvertTo-Json -Depth 6
} catch {
    [ordered]@{ Status = 'BLOCKED'; Reason = $_.Exception.Message } | ConvertTo-Json
    exit 1
} finally {
    foreach ($certificate in $certificates) { $certificate.Dispose() }
}
