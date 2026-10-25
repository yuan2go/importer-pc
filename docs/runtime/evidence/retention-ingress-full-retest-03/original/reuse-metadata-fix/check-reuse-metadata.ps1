$ErrorActionPreference='Stop'
. D:/projects/importer-pc-retention-verification/scripts/project-environment.ps1
$root='D:/projects/ipc-retention-ingress-full-retest-20261025'
$newScript='D:/projects/importer-pc-retention-verification/scripts/verify-retention-maintenance.ps1'
$oldScript=Join-Path $root 'EXECUTED-HELPER.ps1'
$priorCase='D:/ImporterPc.Deployment/Qualification/retention-94f8bd49d7cd4f768117480983e66d2b/case-1000'
$fixture=Join-Path $priorCase 'FIXTURE.json'
$original=Get-Content -LiteralPath $fixture -Raw
$before=(Get-FileHash -LiteralPath $fixture).Hash
$sourceBackup=Join-Path $priorCase 'complete-before-expiry.db'
function Metadata-Block([string]$file){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'Helper syntax failed'}
 $statements=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.PipelineAst] -and $node.Extent.Text -match '^\$sourceFixture \| Add-Member -NotePropertyName (ReusedFromBackup|PriorFixtureSha256) '},$true))
 if($statements.Count -ne 2){throw 'Expected the actual two helper metadata statements'}
 return [ScriptBlock]::Create(($statements.Extent.Text -join [Environment]::NewLine))
}
$old=Metadata-Block $oldScript;$fixed=Metadata-Block $newScript
$sourceFixture=$original|ConvertFrom-Json
if(-not($sourceFixture.PSObject.Properties.Name -contains 'ReusedFromBackup') -or -not($sourceFixture.PSObject.Properties.Name -contains 'PriorFixtureSha256')){throw 'Expected a genuinely reused original fixture'}
$oldFailed=$false
try{. $old}catch{$oldFailed=$true;$failure=$_.Exception.Message}
if(-not $oldFailed){throw 'Pre-fix statements unexpectedly accepted repeated reuse'}
$sourceFixture=$original|ConvertFrom-Json
. $fixed
if($sourceFixture.ReusedFromBackup -ne $sourceBackup -or $sourceFixture.PriorFixtureSha256 -ne $before){throw 'Repeated reuse does not preserve immediate parent identity'}
$first=$original|ConvertFrom-Json
$first.PSObject.Properties.Remove('ReusedFromBackup');$first.PSObject.Properties.Remove('PriorFixtureSha256')
$sourceFixture=$first;. $fixed
if($sourceFixture.ReusedFromBackup -ne $sourceBackup -or $sourceFixture.PriorFixtureSha256 -ne $before){throw 'First reuse failed'}
$sourceFixture=($sourceFixture|ConvertTo-Json -Depth 12)|ConvertFrom-Json
. $fixed
if($sourceFixture.ReusedFromBackup -ne $sourceBackup -or $sourceFixture.PriorFixtureSha256 -ne $before){throw 'Serialized subsequent reuse failed'}
$after=(Get-FileHash -LiteralPath $fixture).Hash
if($before -ne $after){throw 'Original fixture was modified'}
[ordered]@{Status='PASS_REUSE_METADATA_STATEMENTS_ONLY';AtUtc=[DateTimeOffset]::UtcNow.ToString('o');OldFailureReproduced=$oldFailed;OldFailure=$failure;OldScriptSha256=(Get-FileHash -LiteralPath $oldScript).Hash;FixedScriptSha256=(Get-FileHash -LiteralPath $newScript).Hash;OriginalFixtureSha256=$before;OriginalFixtureUnchanged=$true;FirstReuse=$true;RepeatedReuse=$true;SerializedRepeatedReuse=$true;DatabaseOrElevatedCliExecuted=$false;Scope='Actual helper AST metadata statements on an in-memory copy of closed reused fixture; no extra database restore or runtime qualification'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $root 'REUSE-METADATA-CHECK.json') -Encoding utf8NoBOM
Get-Content (Join-Path $root 'REUSE-METADATA-CHECK.json')
