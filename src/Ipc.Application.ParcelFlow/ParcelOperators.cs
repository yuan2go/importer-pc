using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    public async Task<BusinessReceipt> LoginAsync(string subject, string proof)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || ports?.Login is null || repository is null) return new("Rejected", "Login is disabled or application stopped", repository?.Revision ?? 0);
            if (currentOperator is not null || operations.Values.Any(x => !IsSettled(x) && x.Operator?.Subject != subject) || Volatile.Read(ref pendingObservations) != 0) return new("Rejected", "Protect original unfinished operations before changing operator", repository.Revision);
            if (!ExtensionSchema.IsId(subject) || proof is null || proof.Length is < 1 or > 256) return new("Rejected", "Invalid identity input", repository.Revision);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); budget.CancelAfter(Effective.TimeoutMs);
            var identity = await ports.Login.AuthenticateAsync(subject, proof, budget.Token).ConfigureAwait(false);
            if (identity is null) return new("Rejected", "Authentication rejected; no operator changed", repository.Revision);
            if (identity.Subject != subject || string.IsNullOrWhiteSpace(identity.DisplayName) || identity.DisplayName.Length > 128 ||
                identity.DisplayName.Any(char.IsControl) || identity.Authority != Effective.Login!.Target || !identity.Simulation ||
                !Guid.TryParseExact(identity.SessionId, "N", out _) || identity.AuthenticatedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - identity.AuthenticatedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs))
                return new("Rejected", "Authentication evidence is invalid", repository.Revision);
            repository.Commit(null, "OperatorAuthenticated", Wire.Encode(identity)); currentOperator = identity;
            return new("Authenticated", "SIMULATION identity; frozen only when a parcel is admitted", repository.Revision);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        { return new("Unavailable", "Authentication unavailable: " + ex.GetType().Name, repository?.Revision ?? 0); }
        finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> LogoutAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository is null || currentOperator is null || operations.Values.Any(x => !IsSettled(x)) || Volatile.Read(ref pendingObservations) != 0)
                return new("Rejected", "No session or original unfinished operations remain protected", repository?.Revision ?? 0);
            repository.Commit(null, "OperatorLoggedOut", Wire.Encode(currentOperator)); currentOperator = null;
            return new("LoggedOut", "New admission requires authentication; old snapshots retained", repository.Revision);
        }
        finally { gate.Release(); }
    }
}
