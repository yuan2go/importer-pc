using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    bool authenticating;
    public async Task<BusinessReceipt> LoginAsync(string subject, string proof)
    {
        var ownsAuthentication = false;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || stopping.IsCancellationRequested || ports?.Login is null || repository is null) return new("Rejected", "Login is disabled or application stopped", repository?.Revision ?? 0);
            if (!PortMaintenanceClear(ports.Login)) return new("Rejected", "Authentication resource is under maintenance", repository.Revision);
            if (authenticating || session.Valid(DateTimeOffset.UtcNow, Effective.SessionLifetimeMs) || operations.Values.Any(x => !IsSettled(x) && x.Operator?.Subject != subject) || Volatile.Read(ref pendingObservations) != 0) return new("Rejected", "Protect original unfinished operations before changing operator", repository.Revision);
            if (!ExtensionSchema.IsId(subject) || proof is null || proof.Length is < 1 or > 256) return new("Rejected", "Invalid identity input", repository.Revision);
            authenticating = true; ownsAuthentication = true;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); budget.CancelAfter(Effective.TimeoutMs);
            var identity = await ExternalPortAsync(ports.Login, () => ports.Login.AuthenticateAsync(subject, proof, budget.Token)).ConfigureAwait(false);
            if (identity is null) return new("Rejected", "Authentication rejected; no operator changed", repository.Revision);
            if (identity.Subject != subject || string.IsNullOrWhiteSpace(identity.DisplayName) || identity.DisplayName.Length > 128 ||
                identity.DisplayName.Any(char.IsControl) || identity.Authority != Effective.Login!.Target || identity.Simulation != factory.SimulationIdentity(Effective.Login!) || identity.ExpiresAt <= DateTimeOffset.UtcNow ||
                !Guid.TryParseExact(identity.SessionId, "N", out _) || identity.AuthenticatedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - identity.AuthenticatedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs))
                return new("Rejected", "Authentication evidence is invalid", repository.Revision);
            repository.Commit(null, "OperatorAuthenticated", Wire.Encode(identity)); session.Set(Wire.Decode<OperatorIdentity>(Wire.Encode(identity)));
            return new("Authenticated", identity.Simulation ? "SIMULATION identity; frozen only when a parcel is admitted" : "Authenticated provider identity; frozen only at admission", repository.Revision);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        { return new("Unavailable", "Authentication unavailable: " + ex.GetType().Name, repository?.Revision ?? 0); }
        finally { if (ownsAuthentication) authenticating = false; gate.Release(); }
    }
    public async Task<BusinessReceipt> LogoutAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository is null || currentOperator is null || operations.Values.Any(x => !IsSettled(x)) || Volatile.Read(ref pendingObservations) != 0)
                return new("Rejected", "No session or original unfinished operations remain protected", repository?.Revision ?? 0);
            repository.Commit(null, "OperatorLoggedOut", Wire.Encode(currentOperator)); session.Clear();
            return new("LoggedOut", "New admission requires authentication; old snapshots retained", repository.Revision);
        }
        finally { gate.Release(); }
    }
}

public sealed partial class ParcelRuntime
{
    public async Task<BusinessReceipt> AuthorizeMaintenanceAsync(string subject, string proof)
    {
        var accepted = await LoginAdministratorAsync(subject, proof).ConfigureAwait(false);
        return new(accepted ? "Authorized" : "Rejected", accepted ? "软件管理员已登录；配置修改仍需暂停并排空。" : "账号或密码不正确。", 0);
    }
    public async Task<CapabilityCatalog> CapabilitiesAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { Administrator.Require(); return factory.Catalog(); } finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> ManageCapabilityAsync(string action, string identity)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || !CanConfigure) return new("Rejected", "Pause and authenticate maintenance after active calls drain", repository?.Revision ?? 0);
            var actor = AdministratorIdentity!;
            repository!.Commit(null, "ExtensionMaintenanceStarted", Wire.Encode(new { Action = action, Identity = identity, Actor = actor }));
            var result = await ExternalAsync(() => factory.ManageAsync(action, identity)).ConfigureAwait(false);
            repository.Commit(null, "ExtensionMaintenanceResult", Wire.Encode(new { Action = action, Identity = identity, Actor = actor, Result = result }));
            return result with { Revision = repository.Revision };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { repository!.Commit(null, "ExtensionMaintenanceFailed", ex.GetType().Name + ": " + ex.Message); return new("Rejected", ex.Message, repository.Revision); }
        finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository?.Paused != true || !MaintenanceAuthorized || externalWaits != 0) return new("Rejected", "Pause and authenticate maintenance before installation", repository?.Revision ?? 0);
            var actor = AdministratorIdentity;
            repository.Commit(null, "ExtensionInstallStarted", Wire.Encode(new { Actor = actor, Sha256 = sha256 }));
            var result = await ExternalAsync(() => factory.InstallAsync(file, sha256)).ConfigureAwait(false);
            repository.Commit(null, "ExtensionInstallResult", Wire.Encode(new { Actor = actor, Sha256 = sha256, Result = result }));
            return result with { Revision = repository.Revision };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { repository!.Commit(null, "ExtensionInstallFailed", ex.GetType().Name + ": " + ex.Message); return new("Rejected", ex.Message, repository.Revision); }
        finally { gate.Release(); }
    }
}
