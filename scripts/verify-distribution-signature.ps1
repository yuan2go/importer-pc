#requires -Version 7.0
param([Parameter(Mandatory)][string]$Media,[Parameter(Mandatory)][string]$ApprovedSigner)
$ErrorActionPreference='Stop'
try {
 if(-not $IsWindows){throw 'Authenticode verification requires Windows'}
 $certificateFile=Get-Item -LiteralPath $ApprovedSigner
 if($certificateFile.Extension -ne '.cer' -or $certificateFile.Length -gt 1048576){throw 'Provide an externally approved public .cer certificate; no private keys'}
 $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateFile.FullName)
 try {
  $files=@(Get-ChildItem -LiteralPath (Join-Path $Media 'app'),(Join-Path $Media 'desktop') -File | Where-Object {$_.Name -like 'Ipc.*' -and $_.Extension -in '.exe','.dll'})
  if($files.Count -lt 4){throw 'Missing product executables'}
  $signatures=@(foreach($file in $files){
   $signature=Get-AuthenticodeSignature -LiteralPath $file.FullName
   if($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint){throw ('Invalid/unapproved signature: '+$file.Name)}
   [ordered]@{File=[IO.Path]::GetRelativePath($Media,$file.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash;SignerThumbprint=$signature.SignerCertificate.Thumbprint;Status=$signature.Status.ToString()}
  })
  [ordered]@{Status='PASS';Scope='Actual Windows Authenticode chain/status and externally supplied approved public signer; all product PE files';Files=$signatures}|ConvertTo-Json -Depth 5
 } finally {$certificate.Dispose()}
} catch {
 [ordered]@{Status='BLOCKED';Reason=$_.Exception.Message}|ConvertTo-Json
 exit 1
}
