using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    static readonly SemaphoreSlim recoveryGate = new(1, 1);
    static readonly Dictionary<string, ManagementPasswordRecoveryChallenge> pendingRecovery = new(StringComparer.OrdinalIgnoreCase);

    static async Task ResetManagementPassword(BusinessLaunch launch, string password)
    {
        await recoveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(launch.DataRoot);
            if (pendingRecovery.ContainsKey(root))
                throw new InvalidOperationException("上次密码重置结果待确认，请先点击“核对上次重置”。");
            var challenge = await Task.Run(() => LocalManagementMode.ForRoot(root).BeginPasswordRecovery()).ConfigureAwait(false);
            pendingRecovery.Add(root, challenge);
            // Retain the challenge on any failure. The protected service decides
            // whether it committed; a client exception is not proof of rollback.
            await ElevateAsync(new("ResetManagementPassword", Launch: launch, ManagementPassword: password)
            { RecoveryChallenge = challenge }).ConfigureAwait(false);
            pendingRecovery.Remove(root);
        }
        finally { recoveryGate.Release(); }
    }

    static async Task<ManagementPasswordRecoveryReceipt?> InspectManagementPasswordRecovery(BusinessLaunch launch)
    {
        await recoveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(launch.DataRoot);
            if (!pendingRecovery.TryGetValue(root, out var challenge)) return null;
            var json = await ElevateAsync(new("InspectManagementPasswordRecovery", Launch: launch)
            { RecoveryChallenge = challenge }).ConfigureAwait(false);
            var receipt = Wire.Decode<ManagementPasswordRecoveryReceipt>(json ?? throw new IOException("原重置回执缺失。"));
            if (receipt.OperationId != challenge.Id) throw new InvalidDataException("原重置回执身份不匹配。");
            if (receipt.Completed || receipt.CanRetry) pendingRecovery.Remove(root);
            return receipt;
        }
        finally { recoveryGate.Release(); }
    }
}
