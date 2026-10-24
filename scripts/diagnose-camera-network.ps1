#requires -version 5.1
<#
.SYNOPSIS
Read-only Windows camera/network evidence. No application payloads or automatic repairs.
.DESCRIPTION
Output contains private network metadata. Keep it outside Git and review before sharing.
ConfigurationFile should be the application's authoritative configuration.json envelope.
A seed/draft file is labelled as such and never treated as a running connection.
#>
[CmdletBinding()]
param(
    [string[]]$CameraAddress = @(),
    [ValidateRange(1,65535)][int[]]$CameraPort = @(),
    [string]$ConfigurationFile,
    [string]$ReleaseDirectory,
    [string]$OutputDirectory = ('D:\ImporterPcDiagnostics\camera-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')),
    [switch]$EnableIcmpProbe,
    [switch]$EnableTcpProbe,
    [ValidateRange(100,5000)][int]$ProbeTimeoutMs = 1000
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required.' }
if ($CameraAddress.Count -gt 8) { throw 'At most eight explicit camera addresses; no subnet scanning.' }
if ($CameraPort.Count -gt 0 -and $CameraPort.Count -ne $CameraAddress.Count) { throw 'CameraPort must correspond one-to-one to CameraAddress.' }
if (($EnableIcmpProbe -or $EnableTcpProbe) -and $CameraAddress.Count -eq 0) { throw 'Active probes require explicit CameraAddress.' }
if ($EnableTcpProbe -and $CameraPort.Count -ne $CameraAddress.Count) { throw 'Active TCP requires explicit CameraPort; configuration alone never authorizes probes.' }
foreach ($address in $CameraAddress) {
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($address, [ref]$parsed) -or $parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { throw 'CameraAddress must be an explicit IPv4 address.' }
}
if ($OutputDirectory -notmatch '^[dD]:[\\/]' -or [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($OutputDirectory)) -ne 'D:\') { throw 'OutputDirectory must be an absolute local D: path.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing evidence is never overwritten.' }
$errorsFound = New-Object 'System.Collections.Generic.List[object]'
function Read-Section([string]$Name, [scriptblock]$Action) {
    try { @(& $Action) }
    catch { $errorsFound.Add([pscustomobject]@{ Section=$Name; Error=$_.Exception.GetType().Name; Code=$_.FullyQualifiedErrorId }); @() }
}
function Read-Json([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -gt 4MB) { throw 'Evidence file exceeds 4 MiB budget.' }
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}
function IPv4-Mask([int]$PrefixLength) {
    $octets = @()
    for ($i=0; $i -lt 4; $i++) { $bits=[Math]::Max(0,[Math]::Min(8,$PrefixLength-8*$i)); $octets += [string](256-[Math]::Pow(2,8-$bits)) }
    $octets -join '.'
}
$adapters = @(Read-Section 'Adapters' { Get-NetAdapter -IncludeHidden | Select-Object ifIndex,Name,InterfaceDescription,InterfaceGuid,MacAddress,Status,LinkSpeed,Virtual })
$addresses = @(Read-Section 'Addresses' { Get-NetIPAddress | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,IPAddress,PrefixLength,@{Name='IPv4SubnetMask';Expression={ if ([string]$_.AddressFamily -eq 'IPv4') { IPv4-Mask $_.PrefixLength } }},PrefixOrigin,SuffixOrigin,SkipAsSource,AddressState })
$interfaces = @(Read-Section 'IPInterfaces' { Get-NetIPInterface | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,InterfaceMetric,ConnectionState,Dhcp,NlMtu })
$routes = @(Read-Section 'ActiveRoutes' { Get-NetRoute -PolicyStore ActiveStore | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,DestinationPrefix,NextHop,RouteMetric,Protocol,State,PolicyStore })
$persistentRoutes = @(Read-Section 'PersistentRoutes' { Get-NetRoute -PolicyStore PersistentStore | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,DestinationPrefix,NextHop,RouteMetric,Protocol,PolicyStore })
$neighbors = @(Read-Section 'Neighbors' { Get-NetNeighbor | Select-Object InterfaceIndex,InterfaceAlias,IPAddress,LinkLayerAddress,State,PolicyStore })
$connections = @(Read-Section 'TcpConnections' { Get-NetTCPConnection | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess,CreationTime })
$owners = @{}
foreach ($ownerId in @($connections.OwningProcess | Sort-Object -Unique)) {
    $processInfo = @(Read-Section ('Process-' + $ownerId) { Get-Process -Id $ownerId | Select-Object Id,ProcessName,Path,StartTime })
    $owners[[string]$ownerId] = $processInfo
}
$storage = @(Read-Section 'MachineStorageRegistration' {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey('SOFTWARE\ImporterPc\Storage',$false)
        if ($null -ne $key) { try { [pscustomobject]@{ Registry='HKLM\SOFTWARE\ImporterPc\Storage'; Root=$key.GetValue('Root',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); ValidatedByApplication=$false } } finally { $key.Dispose() } }
    } finally { $base.Dispose() }
})
$deployment = @()
if ($ReleaseDirectory) {
    foreach ($relative in @('ASSEMBLY.json','app\application-release.json','DEPENDENCIES.json')) {
        $filePath = Join-Path $ReleaseDirectory $relative
        if (-not (Test-Path -LiteralPath $filePath)) { $errorsFound.Add([pscustomobject]@{ Section=('Deployment-' + $relative); Error='FileNotFoundException'; Code='MissingDeploymentEvidence' }); continue }
        if (Test-Path -LiteralPath $filePath) {
            $deployment += @(Read-Section ('Deployment-' + $relative) {
                $json = Read-Json $filePath
                [pscustomobject]@{ File=$relative; Sha256=(Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash;
                    SourceCommit=$json.SourceCommit; ApplicationId=$json.ApplicationId; BusinessVersion=$json.BusinessVersion; Version=$json.Version; Rid=$json.Rid }
            })
        }
    }
}
$targets = New-Object 'System.Collections.Generic.List[object]'
for ($n=0; $n -lt $CameraAddress.Count; $n++) {
    $port = $null; if ($CameraPort.Count -gt 0) { $port = $CameraPort[$n] }
    $targets.Add([pscustomobject]@{ Origin='Explicit'; DeviceId=$null; Position=$null; Driver=$null; Address=$CameraAddress[$n]; Port=$port })
}
$configSummary = @(Read-Section 'ConfigurationSummary' {
    if ($ConfigurationFile) {
        $envelope = Read-Json $ConfigurationFile
        $effective = $envelope.Effective
        $kind = 'AuthoritativeEnvelope'; if ($null -eq $effective) { $effective=$envelope; $kind='SeedOrCandidate_NOT_EFFECTIVE' }
        $readerList = @($effective.Topology.Readers); if ($readerList.Count -eq 0 -or $null -eq $readerList[0]) { $readerList=@($effective.Reader) }
        $bindings = @()
        foreach ($reader in $readerList) {
            if ($null -eq $reader) { continue }
            $parameters = $reader.Parameters
            if ($reader.Driver -like 'extension:*') { $instanceId=$reader.Driver.Substring(10); $instance=@($effective.Extensions.Instances | Where-Object { $_.Id -eq $instanceId }) | Select-Object -First 1; $parameters=$instance.Parameters }
            $ip = $parameters.'device.ip'; $port=$parameters.'device.port'
            $positions = @($effective.Topology.Positions | Where-Object { $_.ReaderDeviceId -eq $reader.DeviceId } | ForEach-Object { $_.Id })
            $row = [pscustomobject]@{ Origin=$kind; DeviceId=$reader.DeviceId; Position=($positions -join ','); Driver=$reader.Driver; Address=$ip; Port=$port; EntityMode=$parameters.'entity.mode' }
            $bindings += $row
            $parsed = $null
            if ($ip -and [System.Net.IPAddress]::TryParse($ip,[ref]$parsed)) { $targets.Add($row) }
        }
        [pscustomobject]@{ Kind=$kind; FileSha256=(Get-FileHash -LiteralPath $ConfigurationFile -Algorithm SHA256).Hash;
            Revision=$envelope.Revision; Status=$envelope.Status; HasPending=($null -ne $envelope.Pending); ConfirmationRequired=$envelope.Confirmation.Required;
            Schema=$effective.SchemaVersion; Application=$effective.ApplicationId; Mode=$effective.Mode; Readers=$bindings; RunningConnectionsProven=$false }
    }
})
$selectedRoutes = @()
foreach ($target in @($targets | Group-Object Address | ForEach-Object { $_.Group[0] })) {
    $selection = @(Read-Section ('SelectedRoute-' + $target.Address) { Find-NetRoute -RemoteIPAddress $target.Address | Select-Object IPAddress,InterfaceIndex,InterfaceAlias,DestinationPrefix,NextHop,RouteMetric,AddressFamily })
    $selectedRoutes += [pscustomobject]@{ Remote=$target.Address; Selection=$selection;
        Interpretation='OS route/source evidence, not camera identity or business connectivity. Null NextHop means not captured; 0.0.0.0 means on-link.' }
}
$probes = @()
$assessments = @()
foreach ($target in $targets) {
    $matching = @($connections | Where-Object { $_.RemoteAddress -eq $target.Address -and ($null -eq $target.Port -or [string]$_.RemotePort -eq [string]$target.Port) })
    $selection = @($selectedRoutes | Where-Object { $_.Remote -eq $target.Address } | ForEach-Object { $_.Selection })
    $nextStep = '采集时无匹配TCP不能单独证明故障；先核对Application是否运行、Effective绑定与驱动类型，不自动重连或修改路由。'
    if ($target.Origin -eq 'SeedOrCandidate_NOT_EFFECTIVE') { $nextStep = '此文件只是种子或候选；先找到原配置账与实际运行连接，不能据此修改现场网络。' }
    elseif (@($matching | Where-Object { $_.State -eq 'Established' }).Count -gt 0) { $nextStep = '已有TCP连接；核对所属进程、DeviceId/Position/Epoch及协议/最近有效观察。连接不证明业务准入或RCS成功，不占用相机再做探测。' }
    elseif (@($matching | Where-Object { $_.State -eq 'SynSent' }).Count -gt 0) { $nextStep = '本机连接仍在建立；按选中源地址、接口、完整下一跳、预期端口与单客户端合同核查，由授权网络/设备人员确认原因，不通用改metric。' }
    if (@($selection | Where-Object { $_.DestinationPrefix -and $null -ne $_.NextHop }).Count -eq 0) { $nextStep += ' 选路/下一跳证据不全，先补齐只读证据，不判定路由错误。' }
    $assessments += [pscustomobject]@{ DeviceId=$target.DeviceId; Position=$target.Position; Origin=$target.Origin; Address=$target.Address; Port=$target.Port;
        MatchingTcpStates=@($matching.State | Sort-Object -Unique); OwningProcesses=@($matching.OwningProcess | Sort-Object -Unique); NextStep=$nextStep }
}
if ($EnableIcmpProbe -or $EnableTcpProbe) { Write-Warning 'Active probes explicitly enabled. TCP may occupy a single-client camera connection. Pause and coordinate ownership first; no application payload is sent.' }
# Explicit arguments only, even when configuration supplied more addresses.
for ($n=0; $n -lt $CameraAddress.Count; $n++) {
    $ip=$CameraAddress[$n]
    if ($EnableIcmpProbe) {
        $ping = New-Object System.Net.NetworkInformation.Ping
        try { $reply=$ping.Send($ip,$ProbeTimeoutMs); $probes += [pscustomobject]@{ Kind='ICMP'; Address=$ip; Status=[string]$reply.Status; RoundtripMs=$reply.RoundtripTime } }
        catch { $probes += [pscustomobject]@{ Kind='ICMP'; Address=$ip; Status=$_.Exception.GetType().Name } }
        finally { $ping.Dispose() }
    }
    if ($EnableTcpProbe) {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $pendingConnect = $null
        try {
            $pendingConnect=$tcp.BeginConnect($ip,$CameraPort[$n],$null,$null)
            if (-not $pendingConnect.AsyncWaitHandle.WaitOne($ProbeTimeoutMs)) { throw [System.TimeoutException]::new('Connect budget expired') }
            $tcp.EndConnect($pendingConnect)
            $probes += [pscustomobject]@{ Kind='TCP'; Address=$ip; Port=$CameraPort[$n]; Status='Connected'; Local=[string]$tcp.Client.LocalEndPoint; Remote=[string]$tcp.Client.RemoteEndPoint; ApplicationPayloadSent=$false }
        }
        catch { $probes += [pscustomobject]@{ Kind='TCP'; Address=$ip; Port=$CameraPort[$n]; Status=$_.Exception.GetType().Name; ApplicationPayloadSent=$false } }
        finally { $tcp.Close(); if ($null -ne $pendingConnect) { $pendingConnect.AsyncWaitHandle.Close() } }
    }
}
$report = [ordered]@{
    Schema=1; CollectedAtUtc=[DateTime]::UtcNow.ToString('o'); PowerShell=$PSVersionTable.PSVersion.ToString();
    ReadOnlySystemCollection=$true; IcmpProbeEnabled=[bool]$EnableIcmpProbe; TcpProbeEnabled=[bool]$EnableTcpProbe;
    Adapters=$adapters; Addresses=$addresses; IPInterfaces=$interfaces; ActiveRoutes=$routes; PersistentRoutes=$persistentRoutes;
    Neighbors=$neighbors; TcpConnections=$connections; Processes=$owners; MachineStorage=$storage; Deployment=$deployment;
    Configuration=$configSummary; Targets=@($targets.ToArray()); SelectedRoutes=$selectedRoutes; Assessments=$assessments; Probes=$probes; CollectionErrors=@($errorsFound.ToArray())
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'network-evidence.json') -Encoding UTF8
@'
判读与安全修复路径
1. 物理网口编号、InterfaceAlias、InterfaceIndex分别核实；网口4/5/7是现场标签，不能直接作为ifIndex。
2. 按目标IP核对Find-NetRoute的源地址、接口、DestinationPrefix与完整NextHop，结合网卡PrefixLength和Active/Persistent路由。0.0.0.0为on-link；未采集到下一跳不代表没有下一跳。邻居表只说明邻居解析状态。
3. 10.5.2.213曾选10.5.2.210/以太网7/10.5.0.0/22；先确认目标身份、完整下一跳与预期网段，不能直接判错或通用改metric。不得将此历史结果当成当前本机采集事实。
4. TCP连接表只证明采集时刻套接字与进程。SYN_SENT/拒绝/超时、连接被远端关闭、非法UTF-8/帧、IngressRejected、业务Blocked分别沿相机日志、输入审计与原请求核对。ICMP失败也不能独自证明TCP失败。
5. 配置文件的Effective/Pending不是实际套接字证据。核对部署SourceCommit、管理诊断中的DeviceId/Position/Epoch/Local/Remote/Sequence/LastReceivedAt/LastValidObservationAt及原Input→Admission→Operation→Request。最近有效观察不等于实体获准，HTTP心跳不等于业务接受。
6. 发现地址/协议/拓扑不一致：使用现有软件暂停并检查受影响位、保留未知原请求及现场实体，由设备/网络/业务负责人确认合同和独占客户端；通过同一配置账保存并受控激活。重连只用Effective，不重发Unknown。
7. 网络变更必须另获现场授权。先保存现有地址/掩码、接口GUID/Index、路由/metric/下一跳、业务影响和回退命令，由授权网络人员选择最小变更与维护窗口；不要把静态路由、metric、防火墙放行作为默认修复。回退恢复原值并重新核对路由、连接归属和未决实体；不清业务账或换新请求规避责任。
8. 本工具不修改网络、注册表、相机或配置，不扫描网段、不发送应用报文。实际采集可能因权限/系统模块受限；CollectionErrors非空表示证据不全。网络元数据包含私密地址和进程路径，留在Git外，分享前审查。
'@ | Set-Content -LiteralPath (Join-Path $OutputDirectory 'READ-ME.txt') -Encoding UTF8
Write-Host ('Evidence written locally: ' + $OutputDirectory + '; incomplete sections: ' + $errorsFound.Count)
