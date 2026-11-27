using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Ipc.Platform;

// The peer registration is read-only evidence, never this instance's authority.
// Reserve both Effective and Pending so a saved replacement cannot be borrowed
// by the other client while its owner is stopped.
public static class InstallationResourceBoundary
{
    internal static Func<DeploymentLocation?>? OtherInstallationForVerification { get; set; }
    public static DeploymentLocation? OtherInstallation()
    {
        try { return ReadOtherInstallation(); }
        catch (Exception error) when (error is System.Text.Json.JsonException or UnauthorizedAccessException)
        { throw new InvalidDataException("另一实例的受保护登记无法核验，请先修复原安装记录。", error); }
    }
    static DeploymentLocation? ReadOtherInstallation()
    {
        if (OtherInstallationForVerification is { } read) return read();
        if (!OperatingSystem.IsWindows()) return null;
        var anchor = WindowsStoragePaths.ProtectedAnchorFor(!InstallationIdentity.IsTest);
        var application = ProductDeploymentCatalog.IsolatedWorkstation.ApplicationId;
        var file = Path.Combine(anchor, "Machine", "deployments", application + ".json");
        try { _ = File.GetAttributes(file); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        MachineRuntimePolicy.RequireProtectedEvidenceAt(file, anchor);
        if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("另一实例的安装登记超过读取范围。");
        var location = Wire.Decode<DeploymentLocation>(File.ReadAllText(file));
        if (location.ApplicationId != application) throw new InvalidDataException("另一实例的安装身份无法核验。");
        return location;
    }

    internal sealed record Reservations(string[] Ips, string[] Serials, Uri[] Rcs);
    internal static Reservations ReadReservations(DeploymentLocation? location)
    {
        try { return ReadReservationsCore(location); }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        { throw new InvalidDataException("另一实例的配置无法核验，外部连接保持停用，请先修复原配置。", error); }
    }
    static Reservations ReadReservationsCore(DeploymentLocation? location)
    {
        if (location is null || location.LifecycleState == "Purged") return new([], [], []);
        var application = ProductDeploymentCatalog.IsolatedWorkstation.ApplicationId;
        if (location.ApplicationId != application) throw new InvalidDataException("另一实例的安装身份无法核验。");
        var configurations = new List<JsonObject>();
        long bytes = 0;
        var seed = Path.Combine(location.ConfigurationDirectory, "business.json");
        if (Exists(seed)) configurations.Add(ReadJson(seed, ref bytes));
        var count = 0;
        foreach (var mode in Enum.GetNames<RunMode>())
        {
            var root = Path.Combine(location.DataRoot, mode); DeploymentPaths.RequireOrdinaryAncestors(root);
            if (!Exists(root)) continue;
            foreach (var station in Directory.EnumerateDirectories(root))
            {
                if (++count > 128) throw new InvalidDataException("另一实例的站点范围无法核验。");
                var area = Path.Combine(station, application);
                var ledger = Path.Combine(area, "configuration.json");
                if (!Exists(ledger))
                {
                    if (Exists(Path.Combine(area, application + ".db"))) throw new InvalidDataException("另一实例的配置账缺失，不能确认设备归属。");
                    continue;
                }
                var envelope = ReadJson(ledger, ref bytes);
                configurations.Add(envelope["Effective"]?.AsObject() ?? throw new InvalidDataException("另一实例的已生效配置无法核验。"));
                if (envelope["Pending"] is { } pending) configurations.Add(pending.AsObject());
            }
        }
        if (configurations.Count == 0) throw new InvalidDataException("另一实例的配置不可读，先修复原配置再连接测试设备。");
        var ips = new HashSet<string>(StringComparer.Ordinal); var serials = new HashSet<string>(StringComparer.Ordinal);
        var rcs = new List<Uri>();
        foreach (var config in configurations)
        {
            if ((int?)config["SchemaVersion"] != 3 || (string?)config["ApplicationId"] != application)
                throw new InvalidDataException("另一实例的配置不是当前 SM 格式，不能确认设备归属。");
            var primary = config["Reader"]?.AsObject() ?? throw new InvalidDataException("另一实例的相机配置无法核验。");
            var readers = config["Topology"]?["Readers"]?.AsArray();
            foreach (var reader in (readers?.Select(node => node?.AsObject() ?? throw new InvalidDataException("另一实例的相机列表无法核验。")) ?? []).Append(primary))
            {
                if (reader["Parameters"] is not JsonObject parameters)
                {
                    if ((string?)reader["Driver"] is "hikrobot-mvs-reader" or "hikrobot-mvs" or "hikrobot-idmvs")
                        throw new InvalidDataException("另一实例的相机身份参数无法核验。");
                    continue;
                }
                if ((string?)parameters["device.ip"] is { Length: > 0 } ip) ips.Add(ip);
                if ((string?)parameters["device.serial"] is { Length: > 0 } serial) serials.Add(serial);
            }
            var integration = config["Wcs"]?.AsObject() ?? throw new InvalidDataException("另一实例的 RCS 配置无法核验。");
            if ((string?)integration["Target"] is { Length: > 0 } target)
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                    throw new InvalidDataException("另一实例的 RCS 地址无法核验。");
                rcs.Add(endpoint);
            }
        }
        return new(ips.ToArray(), serials.ToArray(), rcs.DistinctBy(endpoint => (endpoint.IdnHost.ToLowerInvariant(), endpoint.Port)).ToArray());
    }
    static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    static JsonObject ReadJson(string file, ref long bytes)
    {
        DeploymentPaths.RequireOrdinaryAncestors(file);
        var length = new FileInfo(file).Length; bytes = checked(bytes + length);
        if (length > 4 * 1024 * 1024 || bytes > 16 * 1024 * 1024) throw new InvalidDataException("另一实例的配置超过读取范围。");
        return JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("另一实例的配置无法核验。");
    }
    public static void RequireCamera(string ip, string? serial = null)
    {
        var reserved = ReadReservations(OtherInstallation());
        if (reserved.Ips.Contains(ip, StringComparer.Ordinal) || serial is { Length: > 0 } && reserved.Serials.Contains(serial, StringComparer.Ordinal))
            throw new InvalidDataException("此相机已属于另一实例，请选择专属测试设备。");
    }
    public static void RequireRcsEndpoint(Uri endpoint)
    {
        var reserved = ReadReservations(OtherInstallation());
        if (reserved.Rcs.Any(peer => peer.Port == endpoint.Port && peer.IdnHost.Equals(endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("此 RCS 地址已属于另一实例，请选择独立的测试 RCS。");
    }
    internal static void RequireDistinctRcsAddresses(int port, IPAddress[] addresses, IEnumerable<(int Port, IPAddress[] Addresses)> peers)
    {
        if (peers.Any(peer => peer.Port == port && peer.Addresses.Any(ip => addresses.Any(target => target.MapToIPv6().Equals(ip.MapToIPv6())))))
            throw new InvalidDataException("此 RCS 与另一实例解析到同一地址，请选择独立的测试 RCS。");
    }
    public static async Task RequireRcsEndpointAsync(Uri endpoint, CancellationToken token)
    {
        var peers = ReadReservations(OtherInstallation()).Rcs.Where(peer => peer.Port == endpoint.Port).ToArray();
        if (peers.Length == 0) return;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.IdnHost, token).ConfigureAwait(false);
        var resolved = new List<(int, IPAddress[])>();
        foreach (var peer in peers) resolved.Add((peer.Port, await Dns.GetHostAddressesAsync(peer.IdnHost, token).ConfigureAwait(false)));
        RequireDistinctRcsAddresses(endpoint.Port, addresses, resolved);
    }
    public static async ValueTask<Stream> ConnectRcsAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var target = context.InitialRequestMessage.RequestUri!;
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
        var resolved = new List<(int, IPAddress[])>();
        foreach (var peer in ReadReservations(OtherInstallation()).Rcs.Where(peer => peer.Port == target.Port))
            resolved.Add((peer.Port, await Dns.GetHostAddressesAsync(peer.IdnHost, token).ConfigureAwait(false)));
        RequireDistinctRcsAddresses(target.Port, addresses, resolved);
        // Connect to the exact addresses just checked; do not resolve the name a
        // second time in the HTTP handler and accidentally cross the boundary.
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
}
