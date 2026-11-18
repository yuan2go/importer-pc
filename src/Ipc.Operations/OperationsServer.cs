using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Operations;

// The passive channel owns only a latest-state subscription and read workers.
// Explicit controls are delegated to a separate, session-owned adapter.
public sealed class OperationsServer : IAsyncDisposable
{
    readonly OperationsInstallation installation;
    readonly CancellationTokenSource stopping = new();
    readonly NamedPipeServerStream[] pipes;
    readonly Task[] workers;
    readonly Task publisher;
    readonly IParcelPresentationSubscription subscription;
    readonly string sid;
    readonly Guid runtime;
    readonly int processId = Environment.ProcessId;
    readonly long startedTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    sealed record Published(OperationsOverview Overview, long Ticks);
    Published? latest;
    string? failure;
    readonly object disposalGate = new();
    Task? disposal;
    readonly OperationsControlServer? control;

    public OperationsServer(OperationsInstallation installation, IParcelPresentationSource source, ParcelSnapshot initial, IParcelApplication? application = null, Func<IStartupMaintenance>? backupFactory = null)
    {
        this.installation = installation; installation.RequireRead(); runtime = initial.RuntimeId;
        if (initial.ApplicationId != "sportmaster" || runtime == Guid.Empty) throw new InvalidDataException("运维快照不属于本站业务。");
        using var identity = WindowsIdentity.GetCurrent(); sid = identity.User!.Value;
        var created = new List<NamedPipeServerStream>();
        IParcelPresentationSubscription? selected = null;
        try
        {
            created.Add(OperationsPipe.Create(installation.PipeName, sid, true));
            created.Add(OperationsPipe.Create(installation.PipeName, sid, false));
            selected = source.SubscribePresentation();
            subscription = selected; pipes = created.ToArray();
            // Initial is already the detached snapshot obtained by the host.
            latest = Capture(initial);
            if (application is not null) control = new(installation, application, runtime, backupFactory);
            publisher = Task.Run(Publish);
            workers = pipes.Select(p => Task.Run(() => Serve(p))).ToArray();
        }
        catch { selected?.Dispose(); foreach (var pipe in created) pipe.Dispose(); stopping.Dispose(); throw; }
    }

    static Published? Capture(ParcelSnapshot snapshot)
    {
        // Preserve the Application's publication age. Draining a delayed subscription
        // or serving another client must never renew stale business evidence.
        var ticks = snapshot.PublishedTicks;
        if (ticks <= 0 || ticks > Stopwatch.GetTimestamp()) return null;
        return new(OperationsProtocol.Project(snapshot, DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(ticks)), ticks);
    }

    async Task Publish()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var update = await subscription.ReadAsync(stopping.Token).ConfigureAwait(false);
                if (update.RuntimeId != runtime || update.Failure is not null)
                { Volatile.Write(ref latest, null); continue; }
                if (update.Snapshot is { } snapshot)
                    Volatile.Write(ref latest, Capture(snapshot));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error) { Volatile.Write(ref failure, error.ToString()); Trace.TraceError("Operations snapshot stopped: {0}", error); }
        finally { Volatile.Write(ref latest, null); }
    }

    async Task Serve(NamedPipeServerStream pipe)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    budget.CancelAfter(TimeSpan.FromSeconds(3));
                    var request = await OperationsPipe.Read<OperationsRequest>(pipe, budget.Token).ConfigureAwait(false);
                    using var peer = OperationsPipe.Peer(pipe, server: false);
                    installation.RequirePeer(peer, server: false);
                    string? clientSid = null;
                    pipe.RunAsClient(() => { using var client = WindowsIdentity.GetCurrent(); clientSid = client.User?.Value; });
                    if (clientSid != sid) throw new UnauthorizedAccessException("只接受当前运行账户的只读连接。");
                    var error = request.Protocol != OperationsProtocol.Version ? "协议版本不一致，请使用同次安装的运维工具。" :
                        request.Installation != installation.Identity ? "安装身份不一致，请重新打开当前安装。" :
                        request.Permission != OperationsProtocol.Permission || request.RequestId == Guid.Empty ? "未授予此操作权限。" :
                        request.RuntimeId != Guid.Empty && request.RuntimeId != runtime ? "运行实例已变化，请重新读取。" : null;
                    var value = Volatile.Read(ref latest);
                    if (error is null && (value is null || Volatile.Read(ref failure) is not null ||
                        Stopwatch.GetElapsedTime(value.Ticks) > OperationsProtocol.Freshness)) error = "工作站状态已失效，请等待重新读取。";
                    await OperationsPipe.Write(pipe, new OperationsReply(OperationsProtocol.Version, installation.Identity,
                        OperationsProtocol.Permission, request.RequestId, runtime, processId, startedTicks,
                        error is null ? value!.Overview : null, error), budget.Token).ConfigureAwait(false);
                    // DisconnectNamedPipe discards unread output. Await a bounded receipt
                    // instead of blocking a thread in WaitForPipeDrain.
                    var acknowledgment = new byte[1]; await pipe.ReadExactlyAsync(acknowledgment, budget.Token).ConfigureAwait(false);
                    if (acknowledgment[0] != 1) throw new InvalidDataException("运维读取确认无效。");
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                    InvalidOperationException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception or ArgumentException or System.Security.SecurityException)
                {
                    // Failed read-only connections own no business responsibility.
                    // Technical diagnostics stay local; no business failure is asserted.
                    Trace.TraceWarning("Operations connection rejected: {0}", error);
                }
                finally
                {
                    if (pipe.IsConnected)
                        try { pipe.Disconnect(); }
                        catch (IOException error) { Trace.TraceWarning("Operations disconnect: {0}", error); }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error) { Volatile.Write(ref failure, error.ToString()); Trace.TraceError("Operations worker stopped: {0}", error); }
    }

    public ValueTask DisposeAsync()
    {
        // Host close and retry paths can overlap. They must drain the same read
        // workers rather than cancel an already-disposed token source.
        lock (disposalGate) return new(disposal is null || disposal.IsFaulted || disposal.IsCanceled ? disposal = Stop() : disposal);
    }

    async Task Stop()
    {
        if (control is not null) await control.DisposeAsync().ConfigureAwait(false);
        stopping.Cancel();
        subscription.Dispose();
        await Task.WhenAll(workers.Append(publisher)).ConfigureAwait(false);
        foreach (var pipe in pipes) pipe.Dispose();
        stopping.Dispose();
    }
}
