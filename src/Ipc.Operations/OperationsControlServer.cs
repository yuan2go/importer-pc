using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Operations;

// Adapts only the shipped SM UI contracts to the already running Application.
// The session lease owns its grant and explicit workspace; no device or DB is
// constructed here. Read-only observers never acquire one of these sessions.
public sealed class OperationsControlServer : IAsyncDisposable
{
    readonly OperationsInstallation installation;
    readonly IParcelApplication application;
    readonly IWorkstationAdministration authority;
    readonly Guid runtime;
    readonly CancellationTokenSource stopping = new();
    readonly ConcurrentDictionary<Guid, Session> sessions = new();
    readonly NamedPipeServerStream[] pipes;
    readonly Task[] workers;
    readonly int pid = Environment.ProcessId;
    readonly long started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    readonly string sid;
    readonly Lazy<IStartupMaintenance>? backups;
    sealed class ForwardProgress(Action<MaintenanceProgress> report) : IProgress<MaintenanceProgress>
    {
        public void Report(MaintenanceProgress value) => report(value);
    }
    sealed class Session(int process, long started)
    {
        internal readonly Guid Id = Guid.NewGuid();
        internal string Workspace = Wire.Id();
        internal string? UiWindow;
        internal readonly int Process = process;
        internal readonly long Started = started;
        internal readonly SemaphoreSlim Serial = new(1);
        internal readonly CancellationTokenSource Lifetime = new();
        internal volatile string? Grant;
        internal volatile bool WorkspaceOwned;
        internal string? WorkspaceGrant;
        internal Task Reclamation = Task.CompletedTask;
        internal readonly HashSet<string> LogSelections = [];
    }

    public OperationsControlServer(OperationsInstallation installation, IParcelApplication application, Guid runtime, Func<IStartupMaintenance>? backupFactory = null)
    {
        this.installation = installation; this.application = application; this.runtime = runtime;
        authority = (IWorkstationAdministration)application;
        if (backupFactory is not null) backups = new(backupFactory);
        using var self = WindowsIdentity.GetCurrent(); sid = self.User!.Value;
        var created = new List<NamedPipeServerStream>();
        try
        {
            for (var i = 0; i < 4; i++) created.Add(OperationsPipe.Create(installation.PipeName + ".control", sid, i == 0, 4));
            pipes = created.ToArray(); workers = pipes.Select(p => Task.Run(() => Serve(p))).ToArray();
        }
        catch { foreach (var pipe in created) pipe.Dispose(); stopping.Dispose(); throw; }
    }

    async Task Serve(NamedPipeServerStream pipe)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                using var headerBudget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                headerBudget.CancelAfter(TimeSpan.FromSeconds(3));
                var request = await OperationsPipe.Read<ControlRequest>(pipe, headerBudget.Token, OperationsControlProtocol.RequestBudget).ConfigureAwait(false);
                using var peer = OperationsPipe.Peer(pipe, false); installation.RequirePeer(peer, false);
                if (OperationsPipe.UserSid(peer) != sid) throw new UnauthorizedAccessException("运行账户不匹配。");
                if (request.Protocol != OperationsControlProtocol.Version || request.Installation != installation.Identity ||
                    request.Runtime != runtime || request.Request == Guid.Empty || string.IsNullOrEmpty(request.Operation) || request.Arguments is null)
                    throw new UnauthorizedAccessException("控制协议、安装或运行实例不匹配。");
                if (request.Operation == OperationsControlProtocol.Open)
                {
                    if (request.Session != Guid.Empty || request.Arguments.Length != 0)
                        throw new UnauthorizedAccessException("运维连接范围无效或已有会话尚未释放。");
                    var session = new Session(peer.Id, peer.StartTime.ToUniversalTime().Ticks);
                    lock (sessions)
                    {
                        if (sessions.Count >= 2 || !sessions.TryAdd(session.Id, session))
                            throw new InvalidOperationException("已有运维会话尚未释放。");
                    }
                    try
                    {
                        await Reply(pipe, request, session.Id, null, null, headerBudget.Token).ConfigureAwait(false);
                        while (!stopping.IsCancellationRequested)
                        {
                            using var pulse = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); pulse.CancelAfter(TimeSpan.FromSeconds(5));
                            var value = new byte[1]; await pipe.ReadExactlyAsync(value, pulse.Token).ConfigureAwait(false);
                            if (value[0] != 1) throw new InvalidDataException("会话确认无效。");
                            installation.RequirePeer(peer, false);
                            await pipe.WriteAsync(value, pulse.Token).ConfigureAwait(false);
                            if (session.WorkspaceOwned && session.Reclamation.IsCompleted)
                                session.Reclamation = ReclaimRevokedWorkspace(session);
                        }
                    }
                    finally { await Release(session).ConfigureAwait(false); }
                }
                else
                {
                    if (!sessions.TryGetValue(request.Session, out var session) || session.Lifetime.IsCancellationRequested ||
                        session.Process != peer.Id || session.Started != peer.StartTime.ToUniversalTime().Ticks)
                        throw new UnauthorizedAccessException("控制会话已失效，请核对原命令结果。");
                    await Execute(pipe, request, session).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception or ArgumentException)
            { Trace.TraceWarning("Operations control connection: {0}", error); }
            finally
            {
                if (pipe.IsConnected) try { pipe.Disconnect(); } catch (IOException error) { Trace.TraceWarning("Control disconnect: {0}", error); }
            }
        }
    }

    async Task Execute(NamedPipeServerStream pipe, ControlRequest request, Session session)
    {
        using var action = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, session.Lifetime.Token);
        action.CancelAfter(request.Operation.StartsWith("ICompleteBackupSetMaintenance.", StringComparison.Ordinal) ||
            request.Operation.StartsWith("IParcelApplication.BackupAsync/", StringComparison.Ordinal) ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(10));
        var ack = new byte[1];
        var receipt = pipe.ReadAsync(ack, action.Token).AsTask();
        // EOF/invalid data cancels only this action's cancellable work. Tasks
        // which crossed their commit/send boundary still drain their real owner.
        var disconnected = receipt.ContinueWith(t => { if (!t.IsCompletedSuccessfully || t.Result != 1 || ack[0] != 1) action.Cancel(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await session.Serial.WaitAsync(action.Token).ConfigureAwait(false);
        try
        {
            var updates = Channel.CreateBounded<MaintenanceProgress>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            async Task PublishProgress()
            {
                await foreach (var progress in updates.Reader.ReadAllAsync(action.Token).ConfigureAwait(false))
                    await OperationsPipe.Write(pipe, new ControlReply(OperationsControlProtocol.Version, installation.Identity, runtime,
                        session.Id, request.Request, pid, started, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement<object?>(null), null, null)
                    { Progress = progress }, action.Token, OperationsControlProtocol.ReplyBudget).ConfigureAwait(false);
            }
            var publishing = PublishProgress();
            object? result = null; Exception? failure = null;
            try { result = await Invoke(request, session, action.Token, new ForwardProgress(p => updates.Writer.TryWrite(p))).ConfigureAwait(false); }
            catch (Exception error)
            {
                failure = error;
                Trace.TraceWarning("Operations command failed; runtime={0}; session={1}; request={2}; operation={3}; {4}",
                    runtime, session.Id, request.Request, request.Operation, DiagnosticText.Protect(error.ToString()));
            }
            finally { updates.Writer.TryComplete(); }
            await publishing.ConfigureAwait(false);
            await Reply(pipe, request, session.Id, result, failure, action.Token).ConfigureAwait(false);
            using var responseBudget = CancellationTokenSource.CreateLinkedTokenSource(action.Token); responseBudget.CancelAfter(TimeSpan.FromSeconds(3));
            var read = await receipt.WaitAsync(responseBudget.Token).ConfigureAwait(false);
            if (read != 1 || ack[0] != 1) throw new InvalidDataException("命令回执未确认。");
        }
        finally
        {
            action.Cancel();
            try { await receipt.ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or IOException) { }
            finally { await disconnected.ConfigureAwait(false); session.Serial.Release(); }
        }
    }

    async Task<object?> Invoke(ControlRequest request, Session session, CancellationToken token, IProgress<MaintenanceProgress> progress)
    {
        installation.RequireRead();
        var name = request.Operation;
        if (name == "Session.ReleaseLogSelection/1")
        {
            if (request.Arguments.Length != 1) throw new InvalidDataException("日志读取身份无效。");
            var selection = request.Arguments[0].GetString() ?? "";
            if (session.LogSelections.Remove(selection)) ((ISoftwareLogSelectionLifetime)application).ReleaseLogSelection(selection);
            return null;
        }
        if (name is "IWorkstationAdministration.InspectManagementPasswordRecoveryAsync/0" or
            "IWorkstationAdministration.ResetManagementPasswordAsync/1" or "IWorkstationAdministration.ConfigureManagementPasswordAsync/1")
            return await OperationsDispatch.Invoke(application, name, request.Arguments, token).ConfigureAwait(false);
        if (name == "IWorkstationAdministration.ManagementModeStatusAsync/0")
        {
            var status = await authority.ManagementModeStatusAsync().ConfigureAwait(false);
            return status.SessionId == session.Grant ? status : status with { Active = false, SessionId = null, ExpiresAt = null };
        }
        if (name == "IWorkstationAdministration.EnterManagementSessionAsync/1")
        {
            if (request.Arguments.Length != 1) throw new InvalidDataException("授权参数无效。");
            var grant = await authority.EnterManagementSessionAsync(request.Arguments[0].GetString() ?? "").ConfigureAwait(false);
            if (grant is { Authorized: true, SessionId: { } id })
            {
                session.Grant = id;
                if (session.WorkspaceOwned && session.WorkspaceGrant != id) await ReleaseWorkspace(session).ConfigureAwait(false);
            }
            return grant;
        }
        if (request.ExpectedGrant != session.Grant)
            throw new UnauthorizedAccessException("发起此操作的管理授权已变化，请重新核对。");
        if (name is "IWorkstationAdministration.ExitManagementModeAsync/0" or "IWorkstationAdministration.ExitManagementModeAsync/1")
        {
            if (session.Grant is { } grant)
            {
                if (request.Arguments.Length == 1 && request.Arguments[0].GetString() != grant) throw new UnauthorizedAccessException("不是本连接取得的授权。");
                await authority.ExitManagementModeAsync(grant).ConfigureAwait(false); session.Grant = null;
                if (session.WorkspaceOwned) await ReleaseWorkspace(session).ConfigureAwait(false);
            }
            return null;
        }
        if (name == "IManagementWorkspace.SetManagementWorkspaceAsync/2")
        {
            if (request.Arguments.Length != 2) throw new InvalidDataException("调试会话参数无效。");
            var open = request.Arguments[1].GetBoolean();
            var window = request.Arguments[0].GetString();
            if (!Guid.TryParseExact(window, "N", out _) || session.WorkspaceOwned && session.UiWindow != window ||
                !open && session.UiWindow is not null && session.UiWindow != window)
                throw new UnauthorizedAccessException("调试窗口不属于本控制连接。");
            if (open) await RequireGrant(session).ConfigureAwait(false);
            if (open && session.WorkspaceOwned && session.WorkspaceGrant != session.Grant)
                throw new InvalidOperationException("原调试资源尚未释放，请先核对并结束原范围。");
            if (open && !session.WorkspaceOwned)
            {
                if (session.UiWindow == window) throw new InvalidOperationException("原调试范围已结束，请明确进入新一轮调试。");
                session.UiWindow = window; session.Workspace = Wire.Id();
            }
            using var authorization = open ? LocalManagementMode.BindCommandSession(request.ExpectedGrant!) : null;
            if (!open && !session.WorkspaceOwned) return new BusinessReceipt("Restored", "本连接没有活动调试。", 0);
            // Retain ownership on an uncertain enter so disconnect cleanup can
            // reconcile exactly this workspace, without touching another one.
            if (open) { session.WorkspaceOwned = true; session.WorkspaceGrant = session.Grant; }
            var result = await ((IManagementWorkspace)application).SetManagementWorkspaceAsync(session.Workspace, open, token).ConfigureAwait(false);
            if (!open && result.Status == "Restored") { session.WorkspaceOwned = false; session.WorkspaceGrant = null; }
            return result;
        }
        await RequireGrant(session).ConfigureAwait(false);
        using var commandAuthorization = LocalManagementMode.BindCommandSession(request.ExpectedGrant!);
        if (name == "IManagementWorkspace.ReadManagementWorkspaceOwnerAsync/0")
        {
            var owner = await ((IManagementWorkspace)application).ReadManagementWorkspaceOwnerAsync(token).ConfigureAwait(false);
            return owner == session.Workspace ? session.UiWindow : null;
        }
        if (name is "ICameraInspection.BeginCameraInspectionAsync/1" or "ICameraInspection.EndCameraInspectionAsync/1" or
            "ICameraInspection.StopCameraInspectionCaptureAsync/2" or "ICameraReadRounds.CloseUnsentDebugRoundAsync/1" or "ICameraReadRounds.ArmCameraReadAsync/1" or
            "ICameraReadRounds.PrepareManualRcsRequestAsync/1" or "ICameraReadRounds.SendCameraRoundAsync/1" or "IParcelApplication.ReconnectDeviceAsync/1")
            if (!session.WorkspaceOwned || session.WorkspaceGrant != session.Grant) throw new UnauthorizedAccessException("请先明确进入设备调试；影响范围为整站自动供包和人工提交。");
        if (name == "IBusinessApplication.CommandAsync/1")
        {
            var command = request.Arguments[0].Deserialize<BusinessCommand>()!;
            var kind = command.Kind;
            // Only the owning pipe may use station-wide debug authority. The local
            // runtime rechecks the disabled lane under its own gate at execution.
            if (kind == "CalibrateEmptyTray" && command.Value != "DisabledLane" &&
                (!session.WorkspaceOwned || session.WorkspaceGrant != session.Grant))
                throw new UnauthorizedAccessException("请先停用要校准的投递位，或从本窗进入整站调试。");
            if (kind is not ("Pause" or "Resume" or "CalibrateEmptyTray" or "ArchiveHistory" or "InspectRestore" or "ConfirmRestore" or
                "IngressResponsibilities" or "InspectInterruptedIngress" or "AcknowledgeOverload" or "AcknowledgeIdentityConflict" or
                "AbandonUnsubmitted" or "ContinuePrepared" or "ContinueUpload" or "ReceiptResponsibilities" or "ReconcileReceipt"))
                throw new UnauthorizedAccessException("此业务命令不属于运维动作白名单。");
        }
        if (name == "IParcelApplication.StageConfigurationAsync/3")
        {
            var expected = request.Arguments[0].GetInt64();
            var candidate = request.Arguments[1].Deserialize<ParcelConfiguration>()!;
            var snapshot = await application.SnapshotAsync().ConfigureAwait(false);
            if (snapshot.Configuration.Revision != expected) throw new InvalidOperationException("配置已由另一窗口修改，原草稿保留。请重新读取并核对差异。");
            OperationsParameterBoundary.RequireTechnical(snapshot.Configuration.Pending ?? snapshot.Configuration.Effective, candidate);
        }
        if (name is "IWorkstationAdministration.QueryLogsAsync/3" or "IWorkstationAdministration.LogContextAsync/4" or "IWorkstationAdministration.ExportLogsAsync/4")
        {
            var selected = name.EndsWith("QueryLogsAsync/3", StringComparison.Ordinal) ? request.Arguments.ElementAtOrDefault(1) : request.Arguments.ElementAtOrDefault(0);
            if (selected.ValueKind == JsonValueKind.String && !session.LogSelections.Contains(selected.GetString()!))
                throw new UnauthorizedAccessException("日志选择不属于本运维连接，请重新查询。");
            if (name.EndsWith("QueryLogsAsync/3", StringComparison.Ordinal) && selected.ValueKind == JsonValueKind.Null && session.LogSelections.Count >= 32)
                throw new InvalidOperationException("本连接保留的日志范围已达预算，请结束原查询后重试。");
        }
        var backupService = name.StartsWith("ICompleteBackupSetMaintenance.", StringComparison.Ordinal) ? backups?.Value : null;
        var value = await OperationsDispatch.Invoke(application, name, request.Arguments, token, backupService, progress).ConfigureAwait(false);
        if (value is SoftwareLogPage page) session.LogSelections.Add(page.Selection);
        return value;
    }

    async Task RequireGrant(Session session)
    {
        var status = await authority.ManagementModeStatusAsync().ConfigureAwait(false);
        if (!status.Authorized || status.SessionId is null || status.SessionId != session.Grant)
            throw new UnauthorizedAccessException("本运维连接的管理授权已失效，原输入和责任保留。");
    }

    async Task ReclaimRevokedWorkspace(Session session)
    {
        try
        {
            if (!await session.Serial.WaitAsync(0, session.Lifetime.Token).ConfigureAwait(false)) return;
            try
            {
                if (!session.WorkspaceOwned || session.Lifetime.IsCancellationRequested) return;
                var status = await authority.ManagementModeStatusAsync().ConfigureAwait(false);
                if (!status.Available || status.Authorized && status.SessionId == session.WorkspaceGrant) return;
                await ReleaseWorkspace(session).ConfigureAwait(false);
            }
            finally { session.Serial.Release(); }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Trace.TraceError("Revoked operations workspace still owns resources: {0}; {1}", session.Workspace, error);
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    Task Reply(Stream pipe, ControlRequest request, Guid session, object? result, Exception? error, CancellationToken token) =>
        OperationsPipe.Write(pipe, new ControlReply(OperationsControlProtocol.Version, installation.Identity, runtime, session, request.Request,
            pid, started, result is ParcelSnapshot { PublishedTicks: > 0 } snapshot ? DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(snapshot.PublishedTicks) : DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(result, result?.GetType() ?? typeof(object)),
            error is null ? null : DiagnosticText.Protect(error.Message), error?.GetType().Name), token, OperationsControlProtocol.ReplyBudget);

    async Task Release(Session session)
    {
        session.Lifetime.Cancel();
        await session.Reclamation.ConfigureAwait(false);
        await session.Serial.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var selection in session.LogSelections) ((ISoftwareLogSelectionLifetime)application).ReleaseLogSelection(selection);
            session.LogSelections.Clear();
            // Only the original workspace identity can restore its acquisition
            // path. The Application preserves pre-existing software pauses.
            while (session.WorkspaceOwned)
            {
                try
                {
                    await ReleaseWorkspace(session).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    Trace.TraceError("Operations workspace release remains unconfirmed: {0}; {1}", session.Workspace, error);
                    // Closing the same workspace is idempotent. Keep its owner
                    // and retry only cleanup; never replay a business send.
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            while (session.Grant is { } grant)
            {
                try { await authority.ExitManagementModeAsync(grant).ConfigureAwait(false); session.Grant = null; }
                catch (Exception error)
                {
                    Trace.TraceError("Operations grant release remains unconfirmed: {0}; {1}", session.Id, error);
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            sessions.TryRemove(session.Id, out _);
        }
        finally { session.Serial.Release(); }
    }

    async Task ReleaseWorkspace(Session session)
    {
        var workspaces = (IManagementWorkspace)application;
        var owner = await workspaces.ReadManagementWorkspaceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
        if (owner == session.Workspace)
        {
            var receipt = await workspaces.SetManagementWorkspaceAsync(session.Workspace, false, CancellationToken.None).ConfigureAwait(false);
            if (receipt.Status != "Restored") throw new InvalidOperationException(receipt.Reason);
        }
        // A verified absent/different owner proves this session does not hold
        // that fence. Never ask the Application to close the other workspace.
        session.WorkspaceOwned = false;
        session.WorkspaceGrant = null;
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        foreach (var pipe in pipes) pipe.Dispose();
        if (backups is { IsValueCreated: true }) await backups.Value.DisposeAsync().ConfigureAwait(false);
        stopping.Dispose();
    }
}
