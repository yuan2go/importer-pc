using System.Security.Cryptography;

namespace Ipc.Platform;

public sealed record ManagementPasswordRecoveryChallenge(string Id, string RootIdentity, long CredentialRevision,
    int InitiatorProcessId, long InitiatorStartedTicks, int WindowsSessionId, DateTimeOffset ExpiresAt, string Nonce);
public sealed record ManagementPasswordRecoveryReceipt(string OperationId, long CredentialRevision, bool Completed, bool CanRetry = false);
internal sealed record ManagementRecoveryCaller(string WindowsSid, bool Administrator, int ProcessId,
    long StartedTicks, int SessionId);

public sealed partial class LocalManagementMode
{
    // Retired protocol: no credential, verifier or local fallback remains.
    public void ResetPassword(string retiredPassword, string newPassword) =>
        throw new UnauthorizedAccessException("共享恢复口令已停用。请由本机Windows管理员重置管理密码。");

    public ManagementPasswordRecoveryChallenge BeginPasswordRecovery()
    {
        if (!Remote) throw new UnauthorizedAccessException("密码恢复需要受保护的Windows管理服务，请修复同版安装。");
        return Rpc<ManagementPasswordRecoveryChallenge>(new("BeginPasswordRecovery"));
    }

    public ManagementPasswordRecoveryReceipt CompletePasswordRecovery(ManagementPasswordRecoveryChallenge challenge, string password)
    {
        if (!Remote) throw new UnauthorizedAccessException("密码恢复需要受保护的Windows管理服务。");
        var receipt = Rpc<ManagementPasswordRecoveryReceipt>(new("CompletePasswordRecovery", Password: password, Recovery: challenge));
        lock (sync) Revoke();
        return receipt;
    }

    public ManagementPasswordRecoveryReceipt InspectPasswordRecovery(string operationId)
    {
        if (!Remote) throw new UnauthorizedAccessException("密码恢复核对需要受保护的Windows管理服务。");
        return Rpc<ManagementPasswordRecoveryReceipt>(new("InspectPasswordRecovery", RequestHash: operationId));
    }

    internal ManagementPasswordRecoveryChallenge IssueRecoveryChallenge(ManagementRecoveryCaller caller)
    {
        if (string.IsNullOrEmpty(caller.WindowsSid) || caller.ProcessId <= 0 || caller.StartedTicks <= 0 || caller.SessionId <= 0)
            throw new UnauthorizedAccessException("无法确认本机恢复发起进程。");
        lock (sync)
        {
            using var held = LockStore();
            var store = Read();
            if (store.Credential is null) throw new InvalidOperationException("尚未设置管理密码，请使用首次设置入口。");
            var pending = (store.RecoveryChallenges ?? []).Where(c => c.ExpiresAt > Now && c.InitiatorProcessId != caller.ProcessId).ToArray();
            if (pending.Length >= 8) throw new InvalidOperationException("密码恢复请求已达上限，请稍后重试。");
            var challenge = new ManagementPasswordRecoveryChallenge(Guid.NewGuid().ToString("N"),
                Wire.Hash(dataRoot.ToUpperInvariant()), store.CredentialRevision, caller.ProcessId, caller.StartedTicks,
                caller.SessionId, Now.AddMinutes(2), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            Save(store with { RecoveryChallenges = [.. pending, challenge] }, "ManagementPasswordRecoveryPrepared");
            return challenge;
        }
    }

    internal ManagementPasswordRecoveryReceipt InspectRecoveryCore(string operationId, ManagementRecoveryCaller caller)
    {
        if (!caller.Administrator) throw new UnauthorizedAccessException("重置和核对管理密码需要Windows管理员权限。");
        lock (sync)
        {
            using var held = LockStore();
            var store = Read();
            return new(operationId, store.LastRecoveryOperation == operationId ? store.LastRecoveryRevision ?? store.CredentialRevision : store.CredentialRevision, store.LastRecoveryOperation == operationId,
                !(store.RecoveryChallenges ?? []).Any(c => c.Id == operationId && c.ExpiresAt > Now));
        }
    }

    internal ManagementPasswordRecoveryReceipt CompleteRecoveryCore(ManagementPasswordRecoveryChallenge challenge,
        string password, ManagementRecoveryCaller caller)
    {
        if (!caller.Administrator || caller.SessionId <= 0 || caller.SessionId != challenge.WindowsSessionId)
            throw new UnauthorizedAccessException("需要与发起窗口同一Windows会话的管理员明确批准。");
        lock (sync)
        {
            using var held = LockStore();
            var store = Read();
            if (store.LastRecoveryOperation == challenge.Id)
                return new(challenge.Id, store.LastRecoveryRevision ?? store.CredentialRevision, true);
            if (store.Credential is null || challenge.RootIdentity != Wire.Hash(dataRoot.ToUpperInvariant()) ||
                challenge.ExpiresAt <= Now || challenge.ExpiresAt > Now.AddMinutes(2) ||
                challenge.CredentialRevision != store.CredentialRevision ||
                !(store.RecoveryChallenges ?? []).Contains(challenge))
                throw new UnauthorizedAccessException("本站恢复确认已过期、已使用或凭据已变化，请重新发起。");
            var credential = Create(password);
            var revision = checked(store.CredentialRevision + 1);
            Save(store with
            {
                Credential = credential, CredentialRevision = revision, LastRecoveryOperation = challenge.Id, LastRecoveryRevision = revision,
                RecoveryChallenges = [], Failures = 0, LockedUntil = default,
                SessionHash = null, SessionId = null, SessionExpires = null, SessionEnteredAt = null, Delegations = []
            }, "ManagementPasswordResetByWindowsAdministrator", "Operation=" + challenge.Id);
            Revoke();
            return new(challenge.Id, revision, true);
        }
    }
}
