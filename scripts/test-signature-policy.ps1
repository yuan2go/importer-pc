#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Reflection.Metadata
Add-Type -AssemblyName System.Security.Cryptography.Pkcs

# Import only the three policy functions, never the release-verification entry
# point. Synthetic ephemeral CMS does not qualify a trusted product signature.
$errors = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-distribution-signature.ps1'), [ref]$null, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Signature verifier has parse errors' }
foreach ($name in @('Assert-OrdinaryPath', 'Assert-CmsPolicy', 'Assert-PeSignatureDigests')) {
    $definition = $syntax.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $definition) { throw "Required policy missing: $name" }
    . ([ScriptBlock]::Create($definition.Extent.Text))
}
$checks = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Output "PASS SIGNATURE-POLICY $Message"
}
function Reject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Check $rejected $Message
}

$key = [Security.Cryptography.RSA]::Create(2048)
$certificate = $null
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synthetic-Policy-Only', $key,
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddMinutes(10))
    $approved = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$approved.Add($certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256))
    function Cms([string]$Digest) {
        $cms = [Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new([byte[]](1, 2, 3)), $false)
        $signer = [Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
        $signer.DigestAlgorithm = [Security.Cryptography.Oid]::new($Digest)
        $cms.ComputeSignature($signer)
        return ,$cms.Encode()
    }
    $sha256 = Cms '2.16.840.1.101.3.4.2.1'
    Assert-CmsPolicy $sha256 $approved
    Check $true 'synthetic SHA256 CMS meets digest/identity policy only'
    $other = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$other.Add('0' * 64)
    Reject { Assert-CmsPolicy $sha256 $other } 'unapproved embedded signer refused'
    $sha1 = Cms '1.3.14.3.2.26'
    Reject { Assert-CmsPolicy $sha1 $approved } 'SHA1 CMS refused despite matching approved identity'
    Reject { Assert-CmsPolicy $sha256 $approved 5 } 'excessive nested signature depth refused'
    Reject { Assert-CmsPolicy ([byte[]](1, 2, 3)) $approved } 'malformed CMS refused'
    # No malformed/unsigned PE may be treated as a verified product file.
    $memory = [IO.MemoryStream]::new([byte[]](77, 90, 0, 0))
    try { Reject { Assert-PeSignatureDigests $memory $approved } 'malformed PE header refused' }
    finally { $memory.Dispose() }
    # A real built PE supplies the table layout. Add synthetic CMS in memory
    # only; the file digest is deliberately not a trusted Authenticode signature.
    $product = Join-Path $PSScriptRoot '../src/Ipc.Platform/bin/Release/net10.0/Ipc.Platform.dll'
    $image = [IO.MemoryStream]::new()
    try {
        $original = [IO.File]::ReadAllBytes($product)
        $image.Write($original)
        $image.Position = 0
        $headers = [Reflection.PortableExecutable.PEReader]::new($image, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
        try {
            $tableOffset = $headers.PEHeaders.PEHeaderStartOffset + $(if ($headers.PEHeaders.PEHeader.Magic.ToString() -eq 'PE32') { 128 } else { 144 })
        } finally { $headers.Dispose() }
        $offset = ($image.Length + [long]7) -band ([long]-8)
        $length = $sha256.Length + 8
        $aligned = ($length + [long]7) -band ([long]-8)
        $image.SetLength($offset + $aligned)
        $writer = [IO.BinaryWriter]::new($image, [Text.Encoding]::UTF8, $true)
        try {
            $image.Position = $tableOffset
            $writer.Write([uint32]$offset); $writer.Write([uint32]$aligned)
            $image.Position = $offset
            $writer.Write([uint32]$length); $writer.Write([uint16]0x200); $writer.Write([uint16]2); $writer.Write([byte[]]$sha256)
            $writer.Flush()
            Assert-PeSignatureDigests $image $approved
            Check $true 'PE certificate table metadata and alignment read from actual PE layout'
            $image.Position = $offset + 6; $writer.Write([uint16]1); $writer.Flush()
            Reject { Assert-PeSignatureDigests $image $approved } 'unsupported certificate entry type refused'
            $image.Position = $tableOffset; $writer.Write([uint32]($image.Length + 8)); $writer.Flush()
            Reject { Assert-PeSignatureDigests $image $approved } 'certificate table outside file refused'
        } finally { $writer.Dispose() }
    } finally { $image.Dispose() }
    Check ($checks -eq 9) 'all nine mandatory negative/positive boundaries executed'
    Write-Output 'SIGNATURE-POLICY: ephemeral CMS/metadata only; actual publisher, SignTool, timestamp trust, revocation and SmartScreen NOT_RUN'
} finally {
    if ($null -ne $certificate) { $certificate.Dispose() }
    $key.Dispose()
}
