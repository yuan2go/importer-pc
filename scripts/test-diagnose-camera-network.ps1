#requires -version 5.1
param([string]$OutputRoot = ('D:\DevCache\importer-pc\temp\dcc01-script-' + [guid]::NewGuid().ToString('N')))
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'diagnose-camera-network.ps1'
$tokens=$null; $parseErrors=$null
$null=[System.Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
function Check([bool]$Condition,[string]$Message) { if (-not $Condition) { throw ('FAIL DCC01 SCRIPT: ' + $Message) }; Write-Host ('PASS DCC01 SCRIPT: ' + $Message) }
function Get-NetAdapter { param([switch]$IncludeHidden) [pscustomobject]@{ ifIndex=71; Name='Synthetic Ethernet 7'; Status='Up' } }
function Get-NetIPAddress { [pscustomobject]@{ InterfaceIndex=71; InterfaceAlias='Synthetic Ethernet 7'; AddressFamily='IPv4'; IPAddress='192.0.2.210'; PrefixLength=22 } }
function Get-NetIPInterface { [pscustomobject]@{ InterfaceIndex=71; InterfaceMetric=25; ConnectionState='Connected'; AddressFamily='IPv4' } }
function Get-NetRoute { param($PolicyStore) [pscustomobject]@{ InterfaceIndex=71; DestinationPrefix='192.0.0.0/22'; NextHop='0.0.0.0'; RouteMetric=256; PolicyStore=$PolicyStore } }
function Get-NetNeighbor { [pscustomobject]@{ InterfaceIndex=71; IPAddress='192.0.2.213'; State='Reachable' } }
function Get-NetTCPConnection { [pscustomobject]@{ LocalAddress='192.0.2.210'; LocalPort=42001; RemoteAddress='192.0.2.213'; RemotePort=1234; OwningProcess=123; State='Established' } }
function Get-Process { param($Id) [pscustomobject]@{ Id=$Id; ProcessName='SyntheticClient'; Path='D:\Synthetic\Client.exe' } }
function Find-NetRoute {
    param($RemoteIPAddress)
    [pscustomobject]@{ IPAddress='192.0.2.210'; InterfaceIndex=71; InterfaceAlias='Synthetic Ethernet 7' }
    [pscustomobject]@{ InterfaceIndex=71; DestinationPrefix='192.0.0.0/22'; NextHop='0.0.0.0'; RouteMetric=256 }
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$configuration=Join-Path $OutputRoot 'configuration.json'
$effective=@{ SchemaVersion=3; ApplicationId='sportmaster'; Mode='Simulation'; Topology=@{ Readers=@(@{ DeviceId='synthetic.left'; Driver='tcp-line'; Parameters=@{ 'device.ip'='192.0.2.213'; 'device.port'='1234'; 'entity.mode'='observe-only'; 'secret'='MUST_NOT_EXPORT' } }); Positions=@(@{ Id='left'; ReaderDeviceId='synthetic.left' }) } }
@{ Revision=5; Status='PendingRestart'; Effective=$effective; Pending=@{ Secret='MUST_NOT_EXPORT' }; Confirmation=@{ Required=$false } } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configuration -Encoding UTF8
& $scriptPath -ConfigurationFile $configuration -OutputDirectory (Join-Path $OutputRoot 'readonly')
$raw=Get-Content -LiteralPath (Join-Path $OutputRoot 'readonly\network-evidence.json') -Raw -Encoding UTF8; $e=$raw | ConvertFrom-Json
Check ($e.PowerShell -like '5.1*') 'actual Windows PowerShell 5.1 executes the collector'
Check (-not $e.IcmpProbeEnabled -and -not $e.TcpProbeEnabled -and @($e.Probes).Count -eq 0) 'default configuration-derived targets never authorize active probes'
Check ($e.Configuration[0].HasPending -and -not $e.Configuration[0].RunningConnectionsProven -and $e.Configuration[0].Readers[0].Position -eq 'left') 'envelope, pending and DeviceId-to-position evidence stay distinct'
Check ($e.Adapters[0].ifIndex -eq 71 -and $e.SelectedRoutes[0].Selection[0].InterfaceIndex -eq 71 -and $e.SelectedRoutes[0].Selection[1].NextHop -eq '0.0.0.0') 'alias, interface index, selected source and complete next hop remain separate'
Check ($e.TcpConnections[0].OwningProcess -eq 123 -and $e.Processes.'123'[0].ProcessName -eq 'SyntheticClient') 'existing TCP sockets map to owning process'
Check ($e.Addresses[0].IPv4SubnetMask -eq '255.255.252.0') 'IPv4 prefix and subnet mask are both explicit'
Check ($e.Assessments[0].NextStep.Contains('已有TCP连接') -and $e.Assessments[0].NextStep.Contains('协议')) 'evidence-based next step distinguishes existing TCP from protocol and business checks'
Check (-not $raw.Contains('MUST_NOT_EXPORT')) 'only allowlisted configuration metadata is exported'
$refused=$false; try { & $scriptPath -EnableTcpProbe -ConfigurationFile $configuration -OutputDirectory (Join-Path $OutputRoot 'wrong') } catch { $refused=$true }
Check $refused 'active probing refuses inferred configuration targets'
$refused=$false; try { & $scriptPath -OutputDirectory (Join-Path $OutputRoot 'readonly') } catch { $refused=$true }
Check $refused 'collector never overwrites earlier evidence'
function Get-NetNeighbor { throw 'Synthetic access limitation' }
& $scriptPath -OutputDirectory (Join-Path $OutputRoot 'limited')
$limited=Get-Content -LiteralPath (Join-Path $OutputRoot 'limited\network-evidence.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check (@($limited.CollectionErrors | Where-Object { $_.Section -eq 'Neighbors' }).Count -eq 1) 'unavailable sections are explicit evidence gaps'
$listener=New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback,0); $listener.Start()
try {
    $port=$listener.LocalEndpoint.Port
    & $scriptPath -CameraAddress '127.0.0.1' -CameraPort $port -EnableTcpProbe -EnableIcmpProbe -OutputDirectory (Join-Path $OutputRoot 'loopback')
    $peer=$listener.AcceptTcpClient()
    try { Check ($peer.GetStream().ReadByte() -eq -1) 'explicit loopback TCP probe closes without sending any application bytes' } finally { $peer.Close() }
    $probe=Get-Content -LiteralPath (Join-Path $OutputRoot 'loopback\network-evidence.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check (@($probe.Probes | Where-Object { $_.Kind -eq 'TCP' -and $_.Status -eq 'Connected' -and -not $_.ApplicationPayloadSent }).Count -eq 1) 'explicit TCP probe reports actual local and remote endpoints'
} finally { $listener.Stop() }
Write-Host 'PASS DCC01 SCRIPT COMPLETE'
