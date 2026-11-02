using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Offline mode contract; never creates a business runtime or device port.
public sealed class ParcelManagementMode(string dataRoot, Func<string, Task>? bootstrapApproval = null, Func<string, Task>? recoveryApproval = null, Func<Task<ManagementPasswordRecoveryReceipt?>>? recoveryInspection = null) : IManagementMode
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
    public Task<ManagementPasswordRecoveryReceipt?> InspectManagementPasswordRecoveryAsync() => recoveryInspection?.Invoke() ?? Task.FromResult<ManagementPasswordRecoveryReceipt?>(null);
    public Task ResetManagementPasswordAsync(string newPassword) => recoveryApproval is null
        ? Task.FromException(new UnauthorizedAccessException("缺少受保护的Windows管理员恢复入口，请使用同版安装修复。"))
        : recoveryApproval(newPassword);
    public Task ResetManagementPasswordAsync(string retiredPassword, string newPassword) => Task.FromException(new UnauthorizedAccessException("共享恢复口令已停用。"));
    public Task ChangeManagementPasswordAsync(string password) => Task.Run(() => Authority.ChangePassword(password));
}
