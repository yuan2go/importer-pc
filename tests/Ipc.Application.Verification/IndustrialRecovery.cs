using Ipc.Platform;

static class IndustrialRecovery
{
    public static void Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL IE01-RECOVERY " + message);
            checks++; Console.WriteLine("PASS IE01-RECOVERY " + message);
        }
        void Deny<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { Check(true, message); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + message);
        }
        var data = Path.Combine(root, "industrial-recovery");
        var mode = new LocalManagementMode(data);
        var ordinary = new ManagementRecoveryCaller("SYNTHETIC-USER", false, 42, 123456, 1);
        var administrator = ordinary with { WindowsSid = "SYNTHETIC-ADMIN", Administrator = true, ProcessId = 43 };
        Deny<InvalidOperationException>(() => mode.IssueRecoveryChallenge(ordinary), "recovery cannot overwrite bootstrap");
        var original = "Synthetic-" + Guid.NewGuid();
        var replacement = "Synthetic-" + Guid.NewGuid();
        mode.ConfigurePassword(original); Check(mode.Enter(original), "real slow-hash credential enters management");
        var delegation = mode.Delegate(Wire.Hash("synthetic-request"));
        var cancellation = mode.Require();
        var challenge = mode.IssueRecoveryChallenge(ordinary);
        var credentialPath = Path.Combine(data, "security", "management-mode.json");
        var before = File.ReadAllBytes(credentialPath);
        Deny<UnauthorizedAccessException>(() => mode.ResetPassword(Guid.NewGuid().ToString(), replacement), "retired password recovery always rejects");
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(challenge, replacement, ordinary), "ordinary caller cannot submit");
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(challenge, replacement, administrator with { SessionId = 2 }), "another Windows session cannot submit");
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(challenge with { RootIdentity = Wire.Hash("another-root") }, replacement, administrator), "challenge cannot target another installation");
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(challenge with { Id = new string('0', 64) }, replacement, administrator), "forged challenge rejected");
        Deny<InvalidDataException>(() => mode.CompleteRecoveryCore(challenge, "1234", administrator), "real password policy enforced before commit");
        Check(before.SequenceEqual(File.ReadAllBytes(credentialPath)), "rejected requests leave protected credential bytes unchanged");
        var receipt = mode.CompleteRecoveryCore(challenge, replacement, administrator);
        Check(receipt.Completed && receipt.CredentialRevision == 2, "administrator submits one atomic credential revision");
        Check(cancellation.IsCancellationRequested && !mode.Status().Authorized, "original grant revoked, reset grants no mode");
        Deny<UnauthorizedAccessException>(() => mode.ValidateDelegation(delegation, delegation.RequestHash, true), "original delegation revoked");
        var committed = File.ReadAllBytes(credentialPath);
        var duplicate = mode.CompleteRecoveryCore(challenge, "different-password", administrator);
        Check(duplicate == receipt && committed.SequenceEqual(File.ReadAllBytes(credentialPath)), "lost-reply retry returns original receipt without changing password");
        Check(!mode.Enter(original) && mode.Enter(replacement), "only replacement credential is accepted");
        Deny<UnauthorizedAccessException>(() => mode.InspectRecoveryCore(challenge.Id, ordinary), "receipt inspection is administrator-only");
        Check(mode.InspectRecoveryCore(challenge.Id, administrator).Completed, "original receipt can be inspected");
        var stale = mode.IssueRecoveryChallenge(ordinary); mode.ChangePassword(original);
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(stale, replacement, administrator), "concurrent password change fences old challenge");
        var now = DateTimeOffset.UtcNow; mode.Clock = () => now;
        var expired = mode.IssueRecoveryChallenge(ordinary); now = now.AddMinutes(3);
        Deny<UnauthorizedAccessException>(() => mode.CompleteRecoveryCore(expired, replacement, administrator), "expired challenge cannot reset");
        File.Delete(credentialPath);
        Deny<InvalidDataException>(() => mode.IssueRecoveryChallenge(ordinary), "missing configured credentials fail closed");
        var release = Path.Combine(root, "epoch-release"); Directory.CreateDirectory(release);
        var declaration = Path.Combine(release, "application-release.json");
        File.WriteAllText(declaration, "{}");
        Deny<InvalidDataException>(() => RecoverySecurityPolicy.RequireRelease(release), "legacy release cannot omit recovery security epoch");
        File.WriteAllText(declaration, "{\"RecoverySecurityEpoch\":0}");
        Deny<InvalidDataException>(() => RecoverySecurityPolicy.RequireRelease(release), "release below security floor rejected");
        File.WriteAllText(declaration, "{\"RecoverySecurityEpoch\":1}");
        RecoverySecurityPolicy.RequireRelease(release); Check(true, "fixed release meets explicit security floor");
        Console.WriteLine("IE01-RECOVERY checks=" + checks + "; synthetic storage and core authorization only; Windows token/UAC NOT_RUN");
    }
}
