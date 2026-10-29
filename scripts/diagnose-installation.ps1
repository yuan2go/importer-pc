param(
    [string]$InstallerPath,
    [string]$OutputDirectory = 'D:\Installer-Diagnostics'
)

# Read-only inspection of installation state. Writes only a new report.
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Run this script in 64-bit Windows PowerShell.' }
if (-not $OutputDirectory.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a report directory on D:.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$reportPath = Join-Path $OutputDirectory ('install-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt')
Set-Content -LiteralPath $reportPath -Encoding UTF8 -Value 'Importer PC installation diagnostic report (no repair performed)'
function Write-Section([string]$Name, [scriptblock]$Read) {
    Add-Content -LiteralPath $reportPath -Encoding UTF8 -Value ("`r`n===== " + $Name + ' =====')
    try {
        $result = & $Read | Out-String -Width 240
        Add-Content -LiteralPath $reportPath -Encoding UTF8 -Value $result
    } catch {
        Add-Content -LiteralPath $reportPath -Encoding UTF8 -Value ('READ FAILED: ' + $_.Exception.ToString())
    }
}

Write-Section 'Windows and caller' {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    [pscustomobject]@{
        Time = (Get-Date).ToString('o'); Product = $os.ProductName
        DisplayVersion = $os.DisplayVersion; Build = $os.CurrentBuild; UBR = $os.UBR
        Is64BitOS = [Environment]::Is64BitOperatingSystem
        IsElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        CommonApplicationData = [Environment]::GetFolderPath('CommonApplicationData')
        TEMP = $env:TEMP; TMP = $env:TMP
    } | Format-List
}

$anchor = $null
Write-Section 'Protected storage registration (read only)' {
    if (Test-Path 'HKLM:\SOFTWARE\ImporterPc\Storage') {
        Get-ItemProperty 'HKLM:\SOFTWARE\ImporterPc\Storage' | Select-Object Root | Format-List
    } else { 'No protected storage registration; application uses Windows default locations.' }
}
if (Test-Path 'HKLM:\SOFTWARE\ImporterPc\Storage') {
    $anchor = (Get-ItemProperty 'HKLM:\SOFTWARE\ImporterPc\Storage').Root
}
$machineRoots = @((Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ImporterPc'), 'D:\ProgramData\ImporterPc')
if ($anchor -and [IO.Path]::IsPathRooted($anchor)) { $machineRoots += Join-Path $anchor 'Machine' }
$dataRoots = New-Object 'System.Collections.Generic.List[string]'
$dataRoots.Add('D:\ProgramData\ImporterPc\data')
$programRoots = New-Object 'System.Collections.Generic.List[string]'
$programRoots.Add('D:\Program Files\CainiaoSupplyStation')
Write-Section 'C and D volumes and current-session drive mappings' {
    foreach ($letter in @('C:', 'D:')) {
        try {
            $volume = New-Object System.IO.DriveInfo($letter)
            [pscustomobject]@{ Drive=$letter; Type=$volume.DriveType; Ready=$volume.IsReady; FileSystem=$volume.DriveFormat; FreeGiB=[Math]::Round($volume.AvailableFreeSpace / 1GB,2) } | Format-List
        } catch { 'VOLUME READ FAILED: ' + $_.Exception.Message }
    }
    & "$env:SystemRoot\System32\subst.exe" 2>&1
    'SUBST ExitCode: ' + $LASTEXITCODE
}
foreach ($machineRoot in ($machineRoots | Select-Object -Unique)) {
    Write-Section ('Recent installation records: ' + $machineRoot) {
        $records = Join-Path $machineRoot 'deployments'
        if (-not (Test-Path -LiteralPath $records)) { 'Directory not present.'; return }
        $files = Get-ChildItem -LiteralPath $records -File -Filter 'sportmaster*.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 8
        foreach ($file in $files) {
            'File: ' + $file.FullName
            'Modified: ' + $file.LastWriteTime.ToString('o')
            try {
                $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                $record | Select-Object Status, ErrorCode, SourceCommit, ReleaseRoot, DataRoot, LifecycleState | Format-List
                if ($record.DataRoot) { $dataRoots.Add([string]$record.DataRoot) }
                if ($record.ReleaseRoot) { $programRoots.Add([string]$record.ReleaseRoot) }
                if ($record.Location) {
                    $record.Location | Select-Object SourceCommit, ReleaseRoot, DataRoot, LifecycleState | Format-List
                    if ($record.Location.DataRoot) { $dataRoots.Add([string]$record.Location.DataRoot) }
                    if ($record.Location.ReleaseRoot) { $programRoots.Add([string]$record.Location.ReleaseRoot) }
                }
                if ($record.Error) { 'Structured original failure:'; $record.Error | ConvertTo-Json -Depth 12 }
                $serviceLog = $file.FullName + '.service.jsonl'
                if (Test-Path -LiteralPath $serviceLog) {
                    'Original service-stage log: ' + $serviceLog
                    if ((Get-Item -LiteralPath $serviceLog).Length -le 1MB) { Get-Content -LiteralPath $serviceLog -Tail 24 }
                    else { 'Service log exceeds the diagnostic read budget; original retained.' }
                }
            } catch { 'RECORD READ FAILED: ' + $_.Exception.ToString() }
        }
    }
}

Write-Section 'Program and data directory attributes and ACLs (read only; not a SYSTEM write test)' {
    $seen = @{}
    foreach ($dataRoot in (($dataRoots.ToArray() + $programRoots.ToArray()) | Select-Object -Unique)) {
        $cursor = [IO.Path]::GetFullPath($dataRoot)
        while ($cursor -and -not $seen.ContainsKey($cursor)) {
            $seen[$cursor] = $true
            'Path: ' + $cursor
            try {
                if (Test-Path -LiteralPath $cursor) {
                    $item = Get-Item -LiteralPath $cursor -Force
                    $item | Select-Object FullName, Attributes, LinkType, Target | Format-List
                    Get-Acl -LiteralPath $cursor | Select-Object Owner, Sddl | Format-List
                    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { 'Reparse point; stopped at this path.'; break }
                } else { 'Not present.' }
            } catch { 'ACL READ FAILED: ' + $_.Exception.Message }
            $parent = [IO.Directory]::GetParent($cursor)
            if ($null -eq $parent) { break }
            $cursor = $parent.FullName
        }
    }
}
$authorityRoots = @((Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ImporterPc.Administrators'))
if ($anchor -and [IO.Path]::IsPathRooted($anchor)) { $authorityRoots += Join-Path $anchor 'Administrators' }
foreach ($authorityRoot in $authorityRoots) {
    Write-Section ('Service-to-data-root anchors: ' + $authorityRoot) {
        if (-not (Test-Path -LiteralPath $authorityRoot)) { 'Not present.'; return }
        foreach ($directory in (Get-ChildItem -LiteralPath $authorityRoot -Directory -Force | Select-Object -First 64)) {
            if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { 'Reparse directory skipped.'; continue }
            $file = Join-Path $directory.FullName 'AUTHORITY.json'
            if ((Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).Length -le 65536) {
                Get-Content -LiteralPath $file -Raw | ConvertFrom-Json | Select-Object DataRoot, Service | Format-List
            }
        }
    }
}
foreach ($dataRoot in ($dataRoots | Select-Object -Unique)) {
    Write-Section ('Service startup errors: ' + $dataRoot) {
        $diagnosticRoot = Join-Path $dataRoot 'security\diagnostics'
        if (-not (Test-Path -LiteralPath $diagnosticRoot)) { 'No startup diagnostic directory.'; return }
        foreach ($file in (Get-ChildItem -LiteralPath $diagnosticRoot -File | Sort-Object LastWriteTime -Descending | Select-Object -First 3)) {
            if ($file.Length -gt 8MB) { 'Oversized diagnostic file retained without reading.'; continue }
            foreach ($line in (Get-Content -LiteralPath $file.FullName -Tail 200)) {
                try {
                    $entry = $line | ConvertFrom-Json
                    if ($entry.Event -eq 'ManagementModeServiceStopped') { $entry | Select-Object At, Event, Detail | Format-List }
                } catch { 'Unparseable diagnostic line retained; no raw content copied.' }
            }
        }
    }
}
Write-Section 'Management services: registry and native SCM queries' {
    $services = @(Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services' | Where-Object { $_.PSChildName -like 'ImporterPc.Administrator.*' })
    if ($services.Count -eq 0) { 'No matching service remains. An unsuccessful new install may have removed its service.' }
    foreach ($service in $services) {
        $name = $service.PSChildName
        Get-ItemProperty -LiteralPath $service.PSPath | Select-Object PSChildName, ImagePath, Start, ObjectName | Format-List
        foreach ($verb in @('queryex', 'qc', 'qfailure', 'qfailureflag')) {
            'sc.exe ' + $verb + ' ' + $name
            & "$env:SystemRoot\System32\sc.exe" $verb $name 2>&1
            'ExitCode: ' + $LASTEXITCODE
        }
    }
}
Write-Section 'Related Windows events in the last 24 hours' {
    foreach ($logName in @('System', 'Application')) {
        'Log: ' + $logName
        try {
            $events = @(Get-WinEvent -FilterHashtable @{ LogName=$logName; StartTime=(Get-Date).AddHours(-24); Level=@(1,2,3) } -MaxEvents 2000 -ErrorAction Stop | Where-Object {
                $_.Message -match 'ImporterPc|Ipc\.Assembly|CainiaoSupplyStation|1783|0x6f7'
            } | Select-Object -First 40)
            if ($events.Count -eq 0) { 'No matching events found within the query limit.' }
            $events | Select-Object TimeCreated, ProviderName, Id, LevelDisplayName, Message | Format-List
        } catch { 'EVENT QUERY FAILED OR NO EVENTS: ' + $_.Exception.Message }
    }
}
Write-Section 'Related SCM creation/state events including informational events' {
    try {
        Get-WinEvent -FilterHashtable @{ LogName='System'; ProviderName='Service Control Manager'; StartTime=(Get-Date).AddHours(-24) } -MaxEvents 1500 -ErrorAction Stop |
            Where-Object { $_.Message -match 'ImporterPc|Ipc\.Assembly|CainiaoSupplyStation|1783|0x6f7|\u4f9b\u5305\u53f0' } |
            Select-Object -First 80 TimeCreated, Id, LevelDisplayName, Message | Format-List
    } catch { 'SCM EVENT QUERY FAILED OR NO EVENTS: ' + $_.Exception.Message }
}
if ($InstallerPath) {
    Write-Section 'Supplied installer identity' {
        Get-Item -LiteralPath $InstallerPath | Select-Object Name, Length, LastWriteTime | Format-List
        Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256 | Format-List
    }
}
Write-Host ('Report saved: ' + $reportPath)
Write-Host 'No service, deployment, credential, ACL or registry setting was changed.'
