using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ipc.Application.ParcelFlow.Desktop;

// First configuration summarizes the actual draft and an optional network-layer check.
// Nothing here creates a device identity, clears a field, touches the camera SDK
// or sends an RCS business request, so no result may read as "可以开始供包".
public sealed record CameraSlot(string Role, string Ip, string Serial)
{
    public bool Filled => Ip.Trim().Length > 0 && Serial.Trim().Length > 0;
    public bool Empty => Ip.Trim().Length == 0 && Serial.Trim().Length == 0;
}
public sealed record ReadinessLine(string Source, string State, bool NeedsAttention);
public sealed record NetworkProbeResult(string Source, string Target, bool Reached, string Text);

public static class FirstConfigurationReadiness
{
    static string Side(string role) => role == "left" ? "左相机" : role == "right" ? "右相机" : role;
    public static ReadinessLine[] Lines(IReadOnlyCollection<CameraSlot> cameras, string rcsTarget)
    {
        var lines = new List<ReadinessLine>();
        foreach (var camera in cameras.OrderBy(c => c.Role == "left" ? 0 : 1))
        {
            lines.Add(camera.Filled
                ? new(Side(camera.Role), "已填写，保存后启动时连接并采图", false)
                : camera.Empty
                    ? new(Side(camera.Role), "未配置，不使用这台相机", false)
                    : camera.Ip.Trim().Length > 0
                        ? new(Side(camera.Role), "已填写 IP，保存时自动读取序列号；请保持相机联网", false)
                        : new(Side(camera.Role), "缺少 IP，请填写相机地址", true));
        }
        var rcs = rcsTarget.Trim().Length > 0;
        lines.Add(rcs ? new("RCS", "已填写地址，保存后启动时连接；心跳确认后才算连通", false) : new("RCS", "尚未配置——没有 RCS 时不能取格口", true));
        lines.Add(rcs ? new("人工输入", "键盘/PDA 补录可用，不依赖相机", false) : new("人工输入", "需先配置 RCS", true));
        return [.. lines];
    }

    // One sentence for the top of the completion step; it names the next action, never readiness to work.
    public static string Summary(IReadOnlyCollection<ReadinessLine> lines)
    {
        var parts = lines.Where(l => l.Source != "人工输入").Select(l => l.Source + (l.State.StartsWith("已填写", StringComparison.Ordinal) ? "已填写" : l.State.StartsWith("未配置", StringComparison.Ordinal) ? "不使用" : "待处理"));
        var next = lines.FirstOrDefault(l => l.NeedsAttention && l.Source != "人工输入");
        return string.Join("；", parts) + (next is null ? "。保存后启动时检查实际连接。" : "——请先处理" + next.Source + "。");
    }

    // Inputs the network result belongs to; any field change makes the old result stale.
    public static string Fingerprint(IReadOnlyCollection<CameraSlot> cameras, string rcsTarget) =>
        string.Join("|", cameras.OrderBy(c => c.Role, StringComparer.Ordinal).Select(c => c.Role + "=" + c.Ip.Trim())) + "|rcs=" + rcsTarget.Trim();

    public static Task<NetworkProbeResult[]> CheckNetworkAsync(IReadOnlyCollection<CameraSlot> cameras, string rcsTarget, CancellationToken token) =>
        CheckNetworkAsync(cameras, rcsTarget, Ping, Connect, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), token);

    // ping/connect are the probes; tests substitute them. Each item and the whole run are bounded.
    public static async Task<NetworkProbeResult[]> CheckNetworkAsync(IReadOnlyCollection<CameraSlot> cameras, string rcsTarget,
        Func<string, CancellationToken, Task<bool>> ping, Func<string, int, CancellationToken, Task<bool>> connect,
        TimeSpan perItem, TimeSpan total, CancellationToken token)
    {
        using var all = CancellationTokenSource.CreateLinkedTokenSource(token); all.CancelAfter(total);
        async Task<bool?> Bounded(Func<CancellationToken, Task<bool>> probe)
        {
            using var item = CancellationTokenSource.CreateLinkedTokenSource(all.Token); item.CancelAfter(perItem);
            try { return await probe(item.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            catch (Exception error) when (error is SocketException or PingException or InvalidOperationException or ArgumentException) { return false; }
        }
        var results = new List<NetworkProbeResult>();
        foreach (var camera in cameras.Where(c => c.Ip.Trim().Length > 0).OrderBy(c => c.Role == "left" ? 0 : 1))
        {
            var ip = camera.Ip.Trim();
            var reached = await Bounded(t => ping(ip, t)).ConfigureAwait(false);
            results.Add(new(Side(camera.Role), ip, reached == true, reached switch
            {
                true => "网络可达。相机身份和采图在保存并启动后检查。",
                null => "网络检查超时，请检查本机网卡和目标地址。相机可能不回应 ping，不一定离线。",
                _ => "网络检查未通过，请检查本机网卡和目标地址。相机可能不回应 ping，不一定离线。"
            }));
        }
        if (rcsTarget.Trim().Length > 0)
        {
            var target = rcsTarget.Trim();
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
                results.Add(new("RCS", target, false, "地址格式不对，请先修正 RCS 服务地址。"));
            else
            {
                var reached = await Bounded(t => connect(uri.Host, uri.Port, t)).ConfigureAwait(false);
                results.Add(new("RCS", uri.Host + ":" + uri.Port, reached == true, reached switch
                {
                    true => "可以连到 RCS 地址。未发送任何业务请求；心跳在保存并启动后确认。内置 RCS 不支持独立的取格口测试。",
                    null => "连接 RCS 地址超时，请检查网络、地址和端口。",
                    _ => "无法连到 RCS 地址，请检查网络、地址和端口。"
                }));
            }
        }
        return [.. results];
    }

    static async Task<bool> Ping(string host, CancellationToken token)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: token).ConfigureAwait(false);
        return reply.Status == IPStatus.Success;
    }

    static async Task<bool> Connect(string host, int port, CancellationToken token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, token).ConfigureAwait(false);
        return client.Connected;
    }
}
