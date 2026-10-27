using System.Security.Cryptography;
using System.Text;

namespace Ipc.Platform;

public sealed partial class LocalManagementMode
{
    // User-authorized fixed local recovery credential; input is never persisted.
    static readonly byte[] RecoveryPasswordHash = Convert.FromHexString("F9C62F903C9F48A46FFF2C2E75F4A7A3E2F03CDD055ACC6C1CE2BC943F7C1C37");
    static bool VerifyRecoveryPassword(string password)
    {
        if (password.Length > 256) return false;
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA256.HashData(bytes);
        try { return CryptographicOperations.FixedTimeEquals(hash, RecoveryPasswordHash); }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(hash); }
    }
    public void ResetPassword(string rootPassword, string newPassword)
    {
        if (Remote) { Rpc<bool>(new("ResetPassword", Password: rootPassword, NewPassword: newPassword)); lock (sync) Revoke(); return; }
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); var now = Now;
            if (s.Credential is null) throw new InvalidOperationException("尚未设置管理密码，请使用首次设置入口。");
            if (s.RecoveryLockedUntil > now) throw new UnauthorizedAccessException("恢复密码连续验证失败，请5分钟后再试。");
            if (!VerifyRecoveryPassword(rootPassword))
            {
                var count = s.RecoveryFailures + 1;
                Save(s with { RecoveryFailures = count >= 5 ? 0 : count, RecoveryLockedUntil = count >= 5 ? now.AddMinutes(5) : default }, "ManagementPasswordRecoveryFailed");
                throw new UnauthorizedAccessException("根密码不正确，请重新输入。");
            }
            var replacement = Create(newPassword);
            Save(s with { Credential = replacement, Failures = 0, LockedUntil = default, RecoveryFailures = 0, RecoveryLockedUntil = default,
                SessionHash = null, SessionId = null, SessionExpires = null, Delegations = [] }, "ManagementPasswordRecovered");
            Revoke();
        }
    }
}
