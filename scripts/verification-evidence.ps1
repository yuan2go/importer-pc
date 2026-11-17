# Frozen verification bytes and native evidence belong to one recorded attempt.
function Save-VerificationBundle([string]$Root,[string]$Destination) {
 if(Test-Path -LiteralPath $Destination){throw 'Frozen bundle already exists'}
 New-Item -ItemType Directory -Path $Destination -ErrorAction Stop | Out-Null
 $entries=@()
 foreach($item in @(@('application','tests/Ipc.Application.Verification/bin/Release/net10.0'),@('native','tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows'),@('desktop','src/Ipc.Desktop/bin/Release/net10.0-windows'))) {
  $source=Join-Path $Root $item[1];$target=Join-Path $Destination $item[0]
  Copy-Item -LiteralPath $source -Destination $target -Recurse -ErrorAction Stop
  foreach($file in Get-ChildItem -LiteralPath $target -File -Recurse){$entries+=@{Path=[IO.Path]::GetRelativePath($Destination,$file.FullName);Hash=(Get-FileHash -LiteralPath $file.FullName).Hash}}
 }
 $entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'inventory.json') -Encoding utf8
}
function Assert-VerificationBundle([string]$Directory) {
 $entries=@(Get-Content -LiteralPath (Join-Path $Directory 'inventory.json') -Raw | ConvertFrom-Json)
 if($entries.Count -eq 0){throw 'Empty frozen bundle'}
 foreach($entry in $entries){if((Get-FileHash -LiteralPath (Join-Path $Directory $entry.Path)).Hash -ne $entry.Hash){throw ('Frozen verification bytes changed: '+$entry.Path)}}
 if(@(Get-ChildItem -LiteralPath $Directory -File -Recurse).Count -ne $entries.Count+1){throw 'Frozen verification inventory changed'}
}
function Reset-NativeEvidence([string]$Root) {
 $pointer=Join-Path $Root '.artifacts/WINDOWS-WPF-EVIDENCE.txt'
 if(Test-Path -LiteralPath $pointer){Remove-Item -LiteralPath $pointer -ErrorAction Stop}
}
function Read-NativeEvidence([string]$Root) {
 $pointer=Join-Path $Root '.artifacts/WINDOWS-WPF-EVIDENCE.txt'
 if(!(Test-Path -LiteralPath $pointer)){throw 'This invocation produced no native evidence pointer; retain its console log'}
 $directory=(Get-Content -LiteralPath $pointer -Raw).Trim()
 if(!(Test-Path -LiteralPath (Join-Path $directory 'WINDOWS-WPF.log'))){throw 'This invocation produced no native evidence log'}
 return $directory
}

function Assert-PublishedBundle([string]$Published,[string]$Bundle) {
 Assert-VerificationBundle $Bundle
 foreach($file in Get-ChildItem -LiteralPath $Published -File -Recurse){
  $relative=[IO.Path]::GetRelativePath($Published,$file.FullName)
  $frozen=Join-Path (Join-Path $Bundle 'desktop') $relative
  if(!(Test-Path -LiteralPath $frozen) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $frozen).Hash){throw ('Published bytes differ from frozen Core output: '+$relative)}
 }
}

function Assert-MediumInventory([string]$Root,[hashtable]$Inventory) {
 if($Inventory.Count -eq 0){throw 'Empty medium inventory'}
 $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($Root);$seen=0
 while($queue.Count){
  $directory=$queue.Dequeue()
  if((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked medium directory refused'}
  foreach($item in Get-ChildItem -LiteralPath $directory -Force){
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked medium entry refused'}
   if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue}
   $name=[IO.Path]::GetRelativePath($Root,$item.FullName).Replace('\','/')
   if($name -eq 'SHA256SUMS'){continue}
   if(!$Inventory.ContainsKey($name)){throw ('Consumed medium file is unlisted: '+$name)}
   if((Get-FileHash -LiteralPath $item.FullName).Hash -ne $Inventory[$name]){throw ('Medium bytes changed: '+$name)}
   $seen++
  }
 }
 if($seen -ne $Inventory.Count){throw 'Medium inventory contains missing or duplicate paths'}
}
