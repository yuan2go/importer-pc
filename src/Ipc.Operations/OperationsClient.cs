using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Ipc.Operations;

public sealed class OperationsClient(OperationsInstallation installation)
{
    // Each sample is a fresh authenticated connection. A failed call immediately
    // invalidates authority; a new runtime is adopted only after its full handshake.
    public async Task<OperationsOverview> ReadAsync(CancellationToken token)
    {
        installation.RequireRead();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", installation.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(budget.Token).ConfigureAwait(false);
        using var peer = OperationsPipe.Peer(pipe, server: true); installation.RequirePeer(peer, server: true);
        using var self = WindowsIdentity.GetCurrent();
        if (OperationsPipe.UserSid(peer) != self.User?.Value) throw new UnauthorizedAccessException("工作站运行账户不匹配。");
        var started = peer.StartTime.ToUniversalTime().Ticks;
        var id = Guid.NewGuid();
        await OperationsPipe.Write(pipe, new OperationsRequest(OperationsProtocol.Version, installation.Identity,
            OperationsProtocol.Permission, id, Guid.Empty), budget.Token).ConfigureAwait(false);
        var reply = await OperationsPipe.Read<OperationsReply>(pipe, budget.Token).ConfigureAwait(false);
        await pipe.WriteAsync(new byte[] { 1 }, budget.Token).ConfigureAwait(false);
        installation.RequirePeer(peer, server: true);
        if (reply.Protocol != OperationsProtocol.Version || reply.Installation != installation.Identity ||
            reply.Permission != OperationsProtocol.Permission || reply.RequestId != id || reply.ProcessId != peer.Id ||
            reply.StartedTicks != started || peer.StartTime.ToUniversalTime().Ticks != started || reply.RuntimeId == Guid.Empty)
            throw new UnauthorizedAccessException("运维连接的安装、版本或运行身份不匹配。");
        if (reply.Error is not null) throw new InvalidOperationException(reply.Error);
        var overview = reply.Overview ?? throw new InvalidDataException("工作站没有提供有效状态。");
        if (overview.RuntimeId != reply.RuntimeId || overview.CapturedAt > DateTimeOffset.UtcNow.AddSeconds(1) ||
            DateTimeOffset.UtcNow - overview.CapturedAt > OperationsProtocol.Freshness)
            throw new InvalidDataException("工作站状态已失效，请重新读取。");
        return overview;
    }
}
