using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ipc.Platform;

public sealed record ManagementModeStatus(bool Configured, bool Active, DateTimeOffset? ExpiresAt, bool HasUnsupportedCredentials = false, bool Available = true)
{ public bool Authorized => Active && ExpiresAt > DateTimeOffset.UtcNow; }
public sealed record ManagementModeEvidence(string SessionId, DateTimeOffset EnteredAt, DateTimeOffset ExpiresAt)
{ [JsonIgnore] public string AuditActor => "management-mode." + SessionId; }
public sealed record ManagementModeDelegation(string Token, string SessionId, string RequestHash, DateTimeOffset ExpiresAt, string Id);

// A station mode grant, never an operator or a hidden administrator account.
public sealed partial class LocalManagementMode
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LocalManagementMode> stores = new(StringComparer.OrdinalIgnoreCase);
    public static LocalManagementMode ForRoot(string root) => stores.GetOrAdd(Path.GetFullPath(root), r => new(r));
    readonly string directory, path, legacyPath, markerPath, dataRoot, diagnosticDirectory;
    readonly bool serviceAuthority;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> storeLocks = new(StringComparer.OrdinalIgnoreCase);
    readonly object sync;
    bool active;
    string? callerWindowsSid;
    string? credentialHash;
    string sessionId = "", sessionSecret = "";
    DateTimeOffset expires;
    CancellationTokenSource revoked = new();
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    DateTimeOffset Now => Clock();
    public T Commit<T>(Func<T> action) { lock (sync) { Require(); return action(); } }
    const int Iterations = 600_000;
    sealed record Credential(string Salt, string Hash);
    sealed record Audit(DateTimeOffset At, string Action, string? ModeSession, string? Detail, string? WindowsSid = null);
    sealed record Delegation(string Id, string TokenHash, string RequestHash, string SessionId, DateTimeOffset ExpiresAt, string SessionHash, bool Consumed = false,
        DateTimeOffset? OperationUntil = null);
    sealed record Store(int SchemaVersion, Credential? Credential, int Failures, DateTimeOffset LockedUntil, Audit[] Audit,
        string? SessionHash = null, string? SessionId = null, DateTimeOffset? SessionExpires = null, Delegation[]? Delegations = null);
    static string ProofHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public LocalManagementMode(string root) : this(root, false) { }
    internal LocalManagementMode(string root, bool serviceAuthority)
    {
        dataRoot = Path.GetFullPath(root); this.serviceAuthority = serviceAuthority;
        if (serviceAuthority)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!current.IsSystem) throw new UnauthorizedAccessException("管理模式授权服务须由Windows服务管理器以System身份启动。");
        }
        directory = serviceAuthority ? ManagementModeBroker.ProtectedRoot(dataRoot) : Path.Combine(dataRoot, "security");
        path = Path.Combine(directory, "management-mode.json"); legacyPath = Path.Combine(directory, "administrators.json"); markerPath = Path.Combine(directory, "MODE-CONFIGURED");
        diagnosticDirectory = Path.Combine(dataRoot, "security", "diagnostics"); sync = storeLocks.GetOrAdd(path, _ => new());
    }
    FileStream LockStore()
    {
        DeploymentPaths.RequireOrdinaryAncestors(path); Directory.CreateDirectory(directory);
        return serviceAuthority && OperatingSystem.IsWindows()
            ? new FileInfo(path + ".lock").Create(FileMode.OpenOrCreate, System.Security.AccessControl.FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))
            : new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    static void ValidateStoredCredential(Credential credential)
    {
        try { if (Convert.FromBase64String(credential.Salt).Length != 32 || Convert.FromBase64String(credential.Hash).Length != 32) throw new InvalidDataException("管理密码哈希格式损坏，保留原件修复。"); }
        catch (FormatException error) { throw new InvalidDataException("管理密码哈希损坏，保留原件修复。", error); }
    }
    Store Read()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(markerPath)) throw new InvalidDataException("已设置的管理密码文件缺失，禁止重新设置覆盖；请由安装维护修复原件。");
            if (File.Exists(legacyPath)) throw new InvalidDataException("旧管理凭据不受支持；请保留原件，禁止转换或重新设置覆盖。");
            if (File.Exists(Path.Combine(directory, "PROVISIONED"))) throw new InvalidDataException("旧版已配置凭据缺失，禁止按首次设置覆盖。");
            return new(2, null, 0, default, []);
        }
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("管理模式凭据超出限制，保留原件修复。");
        var s = JsonSerializer.Deserialize<Store>(File.ReadAllText(path)) ?? throw new InvalidDataException("管理模式凭据损坏。");
        if (s.SchemaVersion != 2 || s.Audit is null || s.Audit.Length > 512 || s.Delegations?.Length > 1024 || s.Credential is null) throw new InvalidDataException("管理模式凭据不完整，禁止重新设置覆盖。");
        if(s.Credential is not null) ValidateStoredCredential(s.Credential);
        return s;
    }
    void Save(Store s, string action, string? detail = null)
    {
        var auditSession = action == "ManagementModeEntered" ? s.SessionId : active ? sessionId : null;
        s = s with { Audit = s.Audit.Append(new(DateTimeOffset.UtcNow, action, auditSession, detail, callerWindowsSid)).TakeLast(512).ToArray() };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = serviceAuthority && OperatingSystem.IsWindows()
                ? new FileInfo(temp).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))
                : new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, s); stream.Flush(true); }
            File.Move(temp, path, true);
            RuntimeJournal.AppendMetadata(diagnosticDirectory, new { At = DateTimeOffset.UtcNow, Source = "管理模式", Level = action.EndsWith("Failed", StringComparison.Ordinal) ? "警告" : "信息", Event = action, ModeSession = auditSession, WindowsSid = callerWindowsSid, Detail = detail });
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void MarkConfigured()
    {
        using var marker = serviceAuthority && OperatingSystem.IsWindows()
            ? new FileInfo(markerPath).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.FullControl, FileShare.Read, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))
            : new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        marker.Flush(true);
    }
    public ManagementModeStatus Status()
    {
        if (Remote) { lock (sync) { if (remoteStatusKnown && !active) return new(knownConfigured, false, null, knownLegacy, knownAvailable); } return RemoteStatus(); }
        lock (sync)
        {
            if (!File.Exists(path) && (File.Exists(legacyPath) || File.Exists(markerPath) || File.Exists(Path.Combine(directory, "PROVISIONED"))))
            { Revoke(); return new(false, false, null, true, false); }
            using var held = LockStore(); var s = Read();
            if (active && !ValidSession(s))
            { try { Save(s, revoked.IsCancellationRequested || Now >= expires ? "ManagementModeExpired" : "ManagementModeRevoked"); } finally { Revoke(); } }
            return new(s.Credential is not null, active, active ? expires : null);
        }
    }
    public ManagementModeStatus InspectStatus() => Remote ? RemoteStatus() : Status();
    public ManagementModeEvidence? Evidence { get { var s = Status(); return s.Authorized ? new(sessionId, expires.AddMinutes(-15), expires) : null; } }
    bool ValidSession(Store s) => active && !revoked.IsCancellationRequested && Now < expires && s.SessionHash == ProofHash(sessionSecret) && s.Credential?.Hash == credentialHash;
    void Revoke() { active = false; sessionSecret = ""; credentialHash = null; revoked.Cancel(); }
    public CancellationToken Require()
    {
        if (!Status().Authorized) throw new UnauthorizedAccessException("请先进入管理模式；作业继续，未保存输入保留。");
        lock (sync) { revoked.Token.ThrowIfCancellationRequested(); return revoked.Token; }
    }
    static Credential Create(string password)
    {
        if (password.Length is < 5 or > 256 || password.Any(char.IsControl)) throw new InvalidDataException("管理密码须为5～256个字符，可使用纯数字，请勿包含控制字符。");
        var salt = RandomNumberGenerator.GetBytes(32);
        return new(Convert.ToBase64String(salt), Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32)));
    }
    static bool Verify(string password, Credential? credential)
    {
        var salt = credential is null ? new byte[32] : Convert.FromBase64String(credential.Salt);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return credential is not null && CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(credential.Hash)); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }
    static void CheckLock(Store s, DateTimeOffset now) { if (s.LockedUntil > now) throw new UnauthorizedAccessException("管理密码连续验证失败，已暂时锁定；请5分钟后再试。"); }
    void Failed(Store s, string action)
    { var count = s.Failures + 1; Save(s with { Failures = count >= 5 ? 0 : count, LockedUntil = count >= 5 ? Now.AddMinutes(5) : default }, action); }
    public void ConfigurePassword(string password)
    {
        if (Remote) { Rpc<bool>(new("ConfigurePassword", Password: password)); return; }
        var credential = Create(password);
        lock (sync) { using var held = LockStore(); var s = Read(); if (s.Credential is not null || File.Exists(legacyPath)) throw new UnauthorizedAccessException("已有管理密码或旧版凭据，不能重新设置覆盖。"); MarkConfigured(); Save(s with { Credential = credential }, "ManagementPasswordConfigured"); }
    }
    public bool Enter(string password)
    {
        if (Remote) return RemoteEnter(password);
        if (password.Length > 256) return false;
        Credential? credential;
        lock (sync) { using var held = LockStore(); var s = Read(); CheckLock(s, Now); if (s.Credential is null) return false; credential = s.Credential; }
        var verified = Verify(password, credential);
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); var now = Now; CheckLock(s, now);
            if (!verified || s.Credential != credential) { Failed(s, "ManagementModeEnterFailed"); return false; }
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var id = Guid.NewGuid().ToString("N");
            Save(s with { Failures = 0, LockedUntil = default, SessionHash = ProofHash(secret), SessionId = id, SessionExpires = now.AddMinutes(15), Delegations = [] }, "ManagementModeEntered");
            Revoke(); revoked = new(); active = true; credentialHash = credential.Hash; sessionId = id; sessionSecret = secret; expires = now.AddMinutes(15); revoked.CancelAfter(TimeSpan.FromMinutes(15)); return true;
        }
    }
    public void Exit()
    {
        if (Remote)
        {
            ManagementModeRpcRequest request;
            lock (sync) { request = new("Exit", Token: sessionSecret); Revoke(); }
            ManagementModeBroker.Call<bool>(dataRoot, request); return;
        }
        lock (sync) { try { using var held = LockStore(); var s = Read(); if (s.Credential is not null) Save(s with { SessionHash = null, SessionId = null, SessionExpires = null, Delegations = [] }, "ManagementModeExited"); } finally { Revoke(); } }
    }
    public void ChangePassword(string password)
    {
        if (Remote) { Rpc<bool>(new("ChangePassword", Password: password)); lock (sync) Revoke(); return; }
        Require(); var replacement = Create(password);
        lock (sync) { using var held = LockStore(); var s = Read(); if (!ValidSession(s)) { Revoke(); throw new UnauthorizedAccessException("管理模式已退出或到期。"); } Save(s with { Credential = replacement, SessionHash = null, SessionId = null, SessionExpires = null, Delegations = [] }, "ManagementPasswordChanged"); Revoke(); }
    }
    public ManagementModeDelegation Delegate(string requestHash) => IssueDelegation(requestHash, null);
    // The two-minute ticket is only admission. Once consumed, this exact
    // operation re-authorizes against the live original session on every check.
    // Neither this lease nor its caller can extend the session or grant replay.
    public ManagementModeDelegation DelegateOperation(string requestHash, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(2) || lifetime.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidDataException("维护持续授权预算须为1秒～2小时；不延长原管理会话。");
        return IssueDelegation(requestHash, lifetime);
    }
    ManagementModeDelegation IssueDelegation(string requestHash, TimeSpan? operationLifetime)
    {
        if (!ExtensionSchema.IsHash(requestHash)) throw new InvalidDataException("维护请求必须使用完整SHA-256身份。");
        if (Remote) return Rpc<ManagementModeDelegation>(new(operationLifetime is null ? "Delegate" : "DelegateOperation", RequestHash: requestHash,
            OperationSeconds: operationLifetime is null ? null : checked((int)operationLifetime.Value.TotalSeconds)));
        Require();
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); if (!ValidSession(s)) { Revoke(); throw new UnauthorizedAccessException("管理模式已退出或到期。"); }
            if (s.Delegations?.Length >= 1024) throw new UnauthorizedAccessException("本次管理模式的维护授权次数已到上限，请重新进入。");
            var grant = new ManagementModeDelegation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), sessionId, requestHash, expires < Now.AddMinutes(2) ? expires : Now.AddMinutes(2), Guid.NewGuid().ToString("N"));
            var operationUntil = operationLifetime is { } duration ? (DateTimeOffset?)(expires < Now + duration ? expires : Now + duration) : null;
            var record = new Delegation(grant.Id, ProofHash(grant.Token), requestHash, sessionId, grant.ExpiresAt, s.SessionHash!, OperationUntil: operationUntil);
            Save(s with { Delegations = (s.Delegations ?? []).Append(record).ToArray() }, "ManagementModeDelegationIssued"); return grant;
        }
    }
    public void ValidateDelegation(ManagementModeDelegation grant, string requestHash, bool consume)
    {
        if (Remote) { Rpc<bool>(new("ValidateDelegation", RequestHash: requestHash, Grant: grant, Consume: consume)); return; }
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); var record = s.Delegations?.SingleOrDefault(d => d.Id == grant.Id);
            var validUntil = !consume && record is { Consumed: true, OperationUntil: { } until } ? until : grant.ExpiresAt;
            if (grant.RequestHash != requestHash || validUntil <= Now || s.Credential is null || s.SessionExpires is not { } sessionExpiry || sessionExpiry <= Now || s.SessionHash is null ||
                record is null || record.RequestHash != requestHash || record.ExpiresAt != grant.ExpiresAt || record.SessionId != grant.SessionId || record.SessionHash != s.SessionHash || grant.Token.Length != 64 || s.SessionId != grant.SessionId ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(record.TokenHash), Encoding.ASCII.GetBytes(ProofHash(grant.Token)))) throw new UnauthorizedAccessException("管理模式授权已失效或与本次维护请求不匹配。");
            if (consume) { if (record.Consumed) throw new UnauthorizedAccessException("维护授权已使用，不能重放。"); Save(s with { Delegations = s.Delegations!.Select(d => d.Id == grant.Id ? d with { Consumed = true } : d).ToArray() }, "ManagementModeDelegationConsumed"); }
            else if (!record.Consumed) throw new UnauthorizedAccessException("维护授权尚未核销。");
        }
    }
}
