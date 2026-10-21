using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Offline account application contract; does not start a runtime or advertise log capabilities.
public sealed class ParcelAdministratorAccounts(string dataRoot, Func<string, string, Task>? bootstrapApproval = null) : IAdministratorAccounts
{
    LocalAdministrator Authority => LocalAdministrator.ForRoot(dataRoot);
    public Task<AdministratorStatus> AdministratorStatusAsync() => Task.Run(Authority.InspectStatus);
    public async Task ProvisionAdministratorAsync(string account, string password)
    {
        try { await Task.Run(() => Authority.Provision(account, password)).ConfigureAwait(false); }
        catch (AdministratorBootstrapRequiredException) when (bootstrapApproval is not null) { await bootstrapApproval(account, password).ConfigureAwait(false); }
    }
    public Task<bool> LoginAdministratorAsync(string account, string password) => Task.Run(() => Authority.Login(account, password));
    public Task LogoutAdministratorAsync() => Task.Run(Authority.Logout);
    public Task<AdministratorAccount[]> AdministratorAccountsAsync() => Task.Run(Authority.Accounts);
    public Task MaintainAdministratorAsync(string account, string? password, bool enabled) => Task.Run(() => Authority.Maintain(account, password, enabled));
}
