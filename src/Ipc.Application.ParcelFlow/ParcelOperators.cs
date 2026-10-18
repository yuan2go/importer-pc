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
            if (authenticating || session.Valid(DateTimeOffset.UtcNow, Effective.SessionLifetimeMs) || operations.Values.Any(x => !IsSettled(x) && x.Operator?.Subject != subject) || Volatile.Read(ref pendingObservations) != 0) return new("Rejected", "Protect original unfinished operations before changing operator", repository.Revision);
            if (!ExtensionSchema.IsId(subject) || proof is null || proof.Length is < 1 or > 256) return new("Rejected", "Invalid identity input", repository.Revision);
            authenticating = true; ownsAuthentication = true;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); budget.CancelAfter(Effective.TimeoutMs);
            var identity = await ExternalAsync(() => ports.Login.AuthenticateAsync(subject, proof, budget.Token)).ConfigureAwait(false);
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
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || stopping.IsCancellationRequested || ports?.Maintenance is null || repository?.Paused != true) return new("Rejected", "Pause before maintenance authentication", repository?.Revision ?? 0);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); budget.CancelAfter(Effective.TimeoutMs);
            var identity = await ExternalAsync(() => ports.Maintenance.AuthenticateAsync(subject, proof, budget.Token)).ConfigureAwait(false);
            if (identity is null || identity.Subject != subject || identity.Authority != (Effective.MaintenanceLogin?.Target ?? "reference://maintenance") ||
                !Guid.TryParseExact(identity.SessionId, "N", out _) || identity.AuthenticatedAt > DateTimeOffset.UtcNow || identity.ExpiresAt is null || identity.Simulation != (Effective.MaintenanceLogin is null || factory.SimulationIdentity(Effective.MaintenanceLogin)) || identity.Grants?.Contains("maintenance") != true || identity.ExpiresAt <= DateTimeOffset.UtcNow)
                return new("Rejected", "Maintenance authentication rejected", repository.Revision);
            maintenanceSession.Set(identity); repository.Commit(null, "MaintenanceAuthenticated", Wire.Encode(identity));
            return new("Authorized", identity.Simulation ? "SIMULATION maintenance grant; pause alone provides no authorization" : "Authenticated maintenance grant; pause alone provides no authorization", repository.Revision);
        }
        finally { gate.Release(); }
    }
    public async Task<CapabilityCatalog> CapabilitiesAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { return factory.Catalog(); } finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository?.Paused != true || !MaintenanceAuthorized || externalWaits != 0) return new("Rejected", "Pause and authenticate maintenance before installation", repository?.Revision ?? 0);
            return await ExternalAsync(() => factory.InstallAsync(file, sha256)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { return new("Rejected", ex.Message, repository?.Revision ?? 0); }
        finally { gate.Release(); }
    }
}
