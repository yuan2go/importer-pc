using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Offline mode contract; never creates a business runtime or device port.
public sealed class ParcelManagementMode(string dataRoot, Func<string, Task>? bootstrapApproval = null) : IManagementMode
{
    LocalManagementMode Authority => LocalManagementMode.ForRoot(dataRoot);
    public Task<ManagementModeStatus> ManagementModeStatusAsync() => Task.Run(Authority.InspectStatus);
    public async Task ConfigureManagementPasswordAsync(string password)
    {
        try { await Task.Run(() => Authority.ConfigurePassword(password)).ConfigureAwait(false); }
        catch (ManagementModeBootstrapRequiredException) when (bootstrapApproval is not null) { await bootstrapApproval(password).ConfigureAwait(false); }
    }
    public Task<bool> EnterManagementModeAsync(string password) => Task.Run(() => Authority.Enter(password));
    public Task ExitManagementModeAsync() => Task.Run(Authority.Exit);
    public Task ResetManagementPasswordAsync(string rootPassword, string newPassword) => Task.Run(() => Authority.ResetPassword(rootPassword, newPassword));
    public Task ChangeManagementPasswordAsync(string password) => Task.Run(() => Authority.ChangePassword(password));
}
