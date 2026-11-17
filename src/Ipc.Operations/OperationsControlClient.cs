using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Operations;

// One connection lease, separate authenticated command connections. A command is
// sent once; loss of its reply never causes a transport retry.
public sealed class OperationsControlClient(OperationsInstallation installation) : IAsyncDisposable
{
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim connect = new(1);
    NamedPipeClientStream? lease;
    Task heartbeat = Task.CompletedTask;
    Guid runtime, session;
    int process;
    long started;
    readonly object cleanupGate = new();
    readonly HashSet<Task> cleanup = [];
    public bool Connected => lease is not null && !heartbeat.IsCompleted;

    async Task<NamedPipeClientStream> Pipe(CancellationToken token)
    {
        installation.RequireRead();
        var pipe = new NamedPipeClientStream(".", installation.PipeName + ".control", PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(budget.Token).ConfigureAwait(false);
            using var peer = OperationsPipe.Peer(pipe, true); installation.RequirePeer(peer, true);
            using var self = WindowsIdentity.GetCurrent();
            if (OperationsPipe.UserSid(peer) != self.User?.Value) throw new UnauthorizedAccessException("工作站运行账户不匹配。");
            if (session != Guid.Empty && (peer.Id != process || peer.StartTime.ToUniversalTime().Ticks != started))
                throw new UnauthorizedAccessException("工作站运行实例已变化，请重新连接并验证授权。");
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }

    public async Task ConnectAsync(CancellationToken token)
    {
        await connect.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Connected) return;
            // Reconnection establishes a new, unprivileged session. The caller
            // must discard old authority; no old command is replayed here.
            lease?.Dispose(); lease = null; session = Guid.Empty;
            var overview = await new OperationsClient(installation).ReadAsync(token).ConfigureAwait(false);
            runtime = overview.RuntimeId;
            var pipe = await Pipe(token).ConfigureAwait(false);
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(3));
                var id = Guid.NewGuid();
                await OperationsPipe.Write(pipe, new ControlRequest(OperationsControlProtocol.Version, installation.Identity,
                    runtime, Guid.Empty, id, OperationsControlProtocol.Open, []), budget.Token, OperationsControlProtocol.RequestBudget).ConfigureAwait(false);
                var reply = await OperationsPipe.Read<ControlReply>(pipe, budget.Token, OperationsControlProtocol.ReplyBudget).ConfigureAwait(false);
                using var peer = OperationsPipe.Peer(pipe, true);
                Validate(reply, id, peer, opening: true);
                session = reply.Session; process = reply.ProcessId; started = reply.StartedTicks;
                lease = pipe; heartbeat = Heartbeat(pipe);
            }
            catch { pipe.Dispose(); throw; }
        }
        finally { connect.Release(); }
    }

    async Task Heartbeat(NamedPipeClientStream pipe)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(4));
                await pipe.WriteAsync(new byte[] { 1 }, budget.Token).ConfigureAwait(false);
                var ack = new byte[1]; await pipe.ReadExactlyAsync(ack, budget.Token).ConfigureAwait(false);
                if (ack[0] != 1) throw new InvalidDataException("运维会话确认无效。");
            } while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        { Trace.TraceWarning("Operations control session lost: {0}", error.Message); }
        finally { pipe.Dispose(); }
    }

    void Validate(ControlReply reply, Guid id, Process peer, bool opening = false)
    {
        installation.RequirePeer(peer, true);
        if (reply.Protocol != OperationsControlProtocol.Version || reply.Installation != installation.Identity || reply.Runtime != runtime ||
            reply.Request != id || reply.ProcessId != peer.Id || reply.StartedTicks != peer.StartTime.ToUniversalTime().Ticks ||
            reply.Session == Guid.Empty || !opening && reply.Session != session)
            throw new UnauthorizedAccessException("运维响应身份不匹配，原命令结果未确认。");
        if (reply.Error is not { } error) return;
        throw reply.ErrorKind switch
        {
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(error),
            nameof(CameraReadRoundConflictException) => new CameraReadRoundConflictException(error),
            nameof(OperationCanceledException) => new OperationCanceledException(error),
            nameof(Ipc.Platform.InstallationOutcomeUnknownException) => new Ipc.Platform.InstallationOutcomeUnknownException(error),
            nameof(Ipc.Platform.MaintenanceCancelledException) => new Ipc.Platform.MaintenanceCancelledException(error),
            nameof(Ipc.Platform.MaintenanceNotStartedException) => new Ipc.Platform.MaintenanceNotStartedException(error, new IOException(error)),
            _ => new InvalidOperationException(error)
        };
    }

    public Task<T> CallAsync<T>(string operation, object?[] arguments, CancellationToken token, IProgress<Ipc.Platform.MaintenanceProgress>? progress = null) =>
        CallCore<T>(operation, arguments, token, progress, null);

    public async Task<T> CallMaintenanceAsync<T>(string operation, object?[] arguments, CancellationToken token, IProgress<Ipc.Platform.MaintenanceProgress>? progress = null)
    {
        var attempted = false;
        try { return await CallCore<T>(operation, arguments, token, progress, () => attempted = true).ConfigureAwait(false); }
        catch (Ipc.Platform.InstallationOutcomeUnknownException) { throw; }
        catch (Ipc.Platform.MaintenanceNotStartedException) { throw; }
        catch (Ipc.Platform.MaintenanceCancelledException) { throw; }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            if (attempted) throw new Ipc.Platform.InstallationOutcomeUnknownException("原维护请求结果未确认；请按原备份、目标与回执核对，不重复提交。", error);
            throw new Ipc.Platform.MaintenanceNotStartedException("尚未提交维护命令；原文件与意图保留，请核对连接后重试。", error);
        }
    }

    async Task<T> CallCore<T>(string operation, object?[] arguments, CancellationToken token, IProgress<Ipc.Platform.MaintenanceProgress>? progress, Action? sending)
    {
        if (!Connected) throw new IOException("运维控制连接已失效；原命令结果未确认，请重新连接并核对原结果。");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        budget.CancelAfter(sending is not null ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(10));
        using var pipe = await Pipe(budget.Token).ConfigureAwait(false);
        using var peer = OperationsPipe.Peer(pipe, true);
        var id = Guid.NewGuid();
        sending?.Invoke();
        await OperationsPipe.Write(pipe, new ControlRequest(OperationsControlProtocol.Version, installation.Identity, runtime, session,
            id, operation, arguments.Select(a => JsonSerializer.SerializeToElement(a, a?.GetType() ?? typeof(object))).ToArray()),
            budget.Token, OperationsControlProtocol.RequestBudget).ConfigureAwait(false);
        ControlReply reply;
        while (true)
        {
            reply = await OperationsPipe.Read<ControlReply>(pipe, budget.Token, OperationsControlProtocol.ReplyBudget).ConfigureAwait(false);
            if (reply.Progress is not { } update) break;
            Validate(reply, id, peer);
            progress?.Report(update);
        }
        await pipe.WriteAsync(new byte[] { 1 }, budget.Token).ConfigureAwait(false);
        Validate(reply, id, peer);
        var result = reply.Result.Deserialize<T>();
        if (result is ParcelSnapshot snapshot)
        {
            var age = DateTimeOffset.UtcNow - reply.ProducedAt;
            if (age < TimeSpan.FromSeconds(-1) || age > OperationsProtocol.Freshness)
                throw new InvalidDataException("控制状态已过期，请重新读取；不得据此执行动作。");
            result = (T)(object)(snapshot with { PublishedTicks = Stopwatch.GetTimestamp() - (long)(Math.Max(0, age.TotalSeconds) * Stopwatch.Frequency) });
        }
        return result!;
    }

    public async Task CallAsync(string operation, object?[] arguments, CancellationToken token) =>
        _ = await CallAsync<object?>(operation, arguments, token).ConfigureAwait(false);

    public void ReleaseLogSelection(string selection)
    {
        lock (cleanupGate)
        {
            cleanup.RemoveWhere(t => t.IsCompleted);
            // Every selection is also session-owned at the server. A lost
            // release is reclaimed when this lease ends, never a data command.
            if (cleanup.Count < 32) cleanup.Add(ReleaseSelection(selection));
        }
    }
    async Task ReleaseSelection(string selection)
    {
        try { await CallAsync("Session.ReleaseLogSelection/1", [selection], lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException)
        { Trace.TraceWarning("Log selection retained by original control session until cleanup: {0}", error.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending; lock (cleanupGate) pending = cleanup.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
        lifetime.Cancel(); lease?.Dispose();
        await heartbeat.ConfigureAwait(false);
        lifetime.Dispose(); connect.Dispose();
    }
}
