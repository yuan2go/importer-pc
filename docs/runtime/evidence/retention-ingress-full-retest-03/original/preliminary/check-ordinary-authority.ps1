$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$evidence='D:/projects/ipc-retention-ingress-full-retest-20261025'
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Use the actual ordinary token for this refusal test'}
$run=Get-Content -LiteralPath (Join-Path $evidence 'protected/PROTECTED-RESULT.json') -Raw|ConvertFrom-Json
$case=$run.Cases|Where-Object Count -eq 1000
if($case.Status -ne 'PASS_PROTECTED_CLI_SYNTHETIC'){throw 'Wait for the closed small case'}
$db=Join-Path $case.CaseRoot 'data/Simulation/retention-qualification/sportmaster/sportmaster.db'
$backup=Join-Path $case.CaseRoot 'ordinary-must-refuse.db'
if(Test-Path -LiteralPath $backup){throw 'New negative destination required'}
$before=(Get-FileHash -LiteralPath $db).Hash
Add-Type 'using System.Runtime.InteropServices; public static class OrdinaryRetentionErrorMode { [DllImport("kernel32.dll")] public static extern uint GetErrorMode(); [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint value); }'
[OrdinaryRetentionErrorMode]::SetErrorMode([OrdinaryRetentionErrorMode]::GetErrorMode() -bor 3)|Out-Null
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $run.ProtectedRoot 'cli/Ipc.Assembly.exe'))
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardError=$true;$start.RedirectStandardOutput=$true
foreach($arg in @('--action','expire-history','--file',$db,'--sha256',$before,'--destination',$backup)){$start.ArgumentList.Add($arg)}
$process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
$out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$code=$process.ExitCode;$process.Dispose()
[IO.File]::WriteAllText((Join-Path $evidence 'ordinary-authority.out.log'),$out,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $evidence 'ordinary-authority.err.log'),$err,[Text.UTF8Encoding]::new($false))
$after=(Get-FileHash -LiteralPath $db).Hash
if($code -eq 0 -or $err -notmatch 'RequireExtensionInstallerAuthority' -or $before -ne $after -or (Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath ($backup+'.recovery'))){throw 'Actual ordinary-token maintenance did not refuse before any database/backup change'}
[ordered]@{Status='PASS_ACTUAL_ORDINARY_TOKEN_REFUSAL';AtUtc=[DateTimeOffset]::UtcNow.ToString('o');SourceCommit=$run.SourceCommit;ProductSha256=$run.ProductSha256;OrdinaryToken=$true;ExitCode=$code;AuthorityGuardInStack=$true;DatabaseBefore=$before;DatabaseAfter=$after;BackupOrRecoveryCreated=$false;Scope='Closed completed 1000-case only; no active large database was read or modified'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'ORDINARY-AUTHORITY.json') -Encoding utf8NoBOM
Get-Content (Join-Path $evidence 'ORDINARY-AUTHORITY.json')
