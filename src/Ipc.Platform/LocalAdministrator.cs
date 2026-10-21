using Ipc.Application.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ipc.Platform;

public sealed record AdministratorStatus(bool Provisioned, string? Account, DateTimeOffset? ExpiresAt)
{ public bool Authorized => Account is not null && ExpiresAt > DateTimeOffset.UtcNow; }
public sealed record AdministratorAccount(string Name, bool Enabled);
public sealed record AdministratorDelegation(string Token, string Account, string RequestHash, DateTimeOffset ExpiresAt, string Id);

// Software authorization is independent of Windows launch qualification and parcel operators.
// The deployment data-root ACL protects the store; no reusable password or default account exists.
public sealed partial class LocalAdministrator
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LocalAdministrator> stores = new(StringComparer.OrdinalIgnoreCase);
    public static LocalAdministrator ForRoot(string root) => stores.GetOrAdd(Path.GetFullPath(root), r => new(r));
    readonly string directory, path, dataRoot, diagnosticDirectory;
    readonly bool serviceAuthority;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> storeLocks = new(StringComparer.OrdinalIgnoreCase);
    readonly object sync;
    string? account, credentialHash;
    string sessionId = "", sessionSecret = "";
    DateTimeOffset expires;
    CancellationTokenSource revoked = new();
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    DateTimeOffset Now => Clock();
    public T Commit<T>(Func<T> action) { lock (sync) { Require(); return action(); } }
    const int Iterations = 600_000;
    sealed record Credential(string Name, string Salt, string Hash, bool Enabled);
    sealed record Audit(DateTimeOffset At, string Action, string? Actor, string? Target);
    sealed record Delegation(string Id, string TokenHash, string RequestHash, string Account, DateTimeOffset ExpiresAt, string SessionHash, bool Consumed = false);
    sealed record Store(Credential[] Accounts, int Failures, DateTimeOffset LockedUntil, Audit[] Audit,
        string? SessionHash = null, string? SessionAccount = null, DateTimeOffset? SessionExpires = null, string[]? UsedDelegations = null, Delegation[]? Delegations = null);
    static string ProofHash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public LocalAdministrator(string root) : this(root, false) { }
    internal LocalAdministrator(string root, bool serviceAuthority)
    {
        dataRoot = Path.GetFullPath(root); this.serviceAuthority = serviceAuthority;
        if (serviceAuthority)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!current.IsSystem) throw new UnauthorizedAccessException("认证服务必须由Windows服务管理器以System身份启动。");
        }
        directory = serviceAuthority ? AdministratorBroker.ProtectedRoot(dataRoot) : Path.Combine(dataRoot, "security"); path = Path.Combine(directory, "administrators.json");
        diagnosticDirectory = Path.Combine(dataRoot, "security", "diagnostics"); sync = storeLocks.GetOrAdd(path, _ => new());
    }
    FileStream LockStore()
    {
        DeploymentPaths.RequireOrdinaryAncestors(path); Directory.CreateDirectory(directory);
        return serviceAuthority && OperatingSystem.IsWindows()
            ? new FileInfo(path + ".lock").Create(FileMode.OpenOrCreate, System.Security.AccessControl.FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))
            : new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    Store Read()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(Path.Combine(directory, "PROVISIONED"))) throw new InvalidDataException("已开通的管理员凭据账缺失；禁止重新开通覆盖，请由安装管理员修复原账。");
            return new([], 0, default, []);
        }
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("管理员凭据账超出限制；保留原文件并联系维护人员。");
        var s = JsonSerializer.Deserialize<Store>(File.ReadAllText(path)) ?? throw new InvalidDataException("管理员凭据账损坏，禁止重新开通覆盖。");
        if (s.Accounts.Length is < 1 or > 64 || s.Accounts.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != s.Accounts.Length || !s.Accounts.Any(a => a.Enabled))
            throw new InvalidDataException("管理员凭据账无效，禁止重新开通覆盖。");
        return s;
    }
    void Save(Store s, string action, string? target)
    {
        s = s with { Audit = s.Audit.Append(new(DateTimeOffset.UtcNow, action, account, target)).TakeLast(512).ToArray() };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = serviceAuthority && OperatingSystem.IsWindows()
                ? new FileInfo(temp).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))
                : new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, s); stream.Flush(true); }
            File.Move(temp, path, true);
            RuntimeJournal.AppendMetadata(diagnosticDirectory, new { At = DateTimeOffset.UtcNow, Source = "认证", Level = action == "AdministratorLoginFailed" ? "警告" : "信息", Event = action, Actor = account, TargetAccount = target });
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public AdministratorStatus Status()
    {
        if (Remote) { lock (sync) { if (remoteStatusKnown && account is null) return new(knownProvisioned, null, null); } return RemoteStatus(); }
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (account is not null && !ValidSession(s))
            { try { Save(s, Now >= expires ? "AdministratorSessionExpired" : "AdministratorSessionRevoked", account); } finally { Revoke(); } }
            return new(s.Accounts.Length > 0, account, account is null ? null : expires);
        }
    }
    public AdministratorStatus InspectStatus() => Remote ? RemoteStatus() : Status();
    public OperatorIdentity? Identity
    {
        get { var s = Status(); return s.Authorized ? new(s.Account!, s.Account!, "software-administrator", sessionId, expires.AddMinutes(-15), false, expires, ["maintenance"]) : null; }
    }
    bool ValidSession(Store s) => account is not null && Now < expires && s.SessionHash == ProofHash(sessionSecret) && s.Accounts.Any(a => a.Enabled && a.Name == account && a.Hash == credentialHash);
    void Revoke() { account = null; sessionSecret = ""; credentialHash = null; revoked.Cancel(); }
    public CancellationToken Require()
    {
        if (!Status().Authorized) throw new UnauthorizedAccessException("请先登录软件管理员；原作业继续，未保存输入保留。");
        lock (sync) { revoked.Token.ThrowIfCancellationRequested(); return revoked.Token; }
    }
    static void Validate(string name, string password)
    {
        if (!Regex.IsMatch(name, "\\A[A-Za-z0-9][A-Za-z0-9._-]{2,63}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25))) throw new InvalidDataException("账号须为3～64位字母、数字、点、下划线或短横线。");
        if (password.Length is < 12 or > 256 || password.Any(char.IsControl) || password.Distinct().Count() < 5) throw new InvalidDataException("密码须为12～256个字符，并至少使用5种不同字符。");
    }
    static Credential Create(string name, string password)
    {
        Validate(name, password); var salt = RandomNumberGenerator.GetBytes(32);
        return new(name, Convert.ToBase64String(salt), Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32)), true);
    }
    public void Provision(string name, string password)
    {
        if (Remote) { Rpc<bool>(new("Provision", Account: name, Password: password)); return; }
        var credential = Create(name, password);
        lock (sync) { using var held = LockStore(); var s = Read(); if (s.Accounts.Length != 0) throw new UnauthorizedAccessException("已开通管理员，请使用登录；不能覆盖原账号。"); using (var marker = new FileStream(Path.Combine(directory, "PROVISIONED"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) marker.Flush(true); Save(s with { Accounts = [credential] }, "AdministratorProvisioned", name); }
    }
    public bool Login(string name, string password)
    {
        if (Remote) return RemoteLogin(name, password);
        if (name.Length > 64 || password.Length > 256) return false;
        Credential? found;
        lock (sync)
        {
            using var held = LockStore(); var original = Read(); if (original.Accounts.Length == 0) return false;
            if (original.LockedUntil > Now) throw new UnauthorizedAccessException("连续登录失败，已暂时锁定；请5分钟后再试。");
            found = original.Accounts.SingleOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        // Expensive password derivation must never hold the shared authorization/store lock.
        var salt = found is null ? new byte[32] : Convert.FromBase64String(found.Salt);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var verified = found?.Enabled == true && CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(found.Hash));
        CryptographicOperations.ZeroMemory(actual);
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); var now = Now;
            if (s.LockedUntil > now) throw new UnauthorizedAccessException("连续登录失败，已暂时锁定；请5分钟后再试。");
            if (!verified || !s.Accounts.Any(c => c.Enabled && c.Name == found!.Name && c.Hash == found.Hash))
            { var count = s.Failures + 1; Save(s with { Failures = count >= 5 ? 0 : count, LockedUntil = count >= 5 ? now.AddMinutes(5) : default }, "AdministratorLoginFailed", null); return false; }
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Save(s with { Failures = 0, LockedUntil = default, SessionHash = ProofHash(secret), SessionAccount = found!.Name, SessionExpires = now.AddMinutes(15), UsedDelegations = [], Delegations = [] }, "AdministratorLoggedIn", found!.Name);
            Revoke(); revoked = new(); account = found!.Name; credentialHash = found.Hash; sessionId = Guid.NewGuid().ToString("N"); sessionSecret = secret; expires = now.AddMinutes(15); revoked.CancelAfter(TimeSpan.FromMinutes(15)); return true;
        }
    }
    public void Logout()
    { if (Remote) { try { Rpc<bool>(new("Logout")); } finally { lock (sync) Revoke(); } return; } lock (sync) { try { using var held = LockStore(); var s = Read(); if (s.Accounts.Length > 0) Save(s with { SessionHash = null, SessionAccount = null, SessionExpires = null }, "AdministratorLoggedOut", account); } finally { Revoke(); } } }
    public AdministratorAccount[] Accounts()
    { if (Remote) return Rpc<AdministratorAccount[]>(new("Accounts")); Require(); lock (sync) { using var held = LockStore(); var s = Read(); if (!ValidSession(s)) { Revoke(); throw new UnauthorizedAccessException("管理员会话已失效。"); } return s.Accounts.Select(a => new AdministratorAccount(a.Name, a.Enabled)).ToArray(); } }
    public void Maintain(string name, string? password, bool enabled)
    {
        if (Remote) { Rpc<bool>(new("Maintain", Account: name, Password: password, Enabled: enabled)); lock (sync) Revoke(); return; }
        Require(); var replacement = password is null ? null : Create(name, password);
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (!ValidSession(s)) { Revoke(); throw new UnauthorizedAccessException("管理员会话已失效。"); }
            var existing = s.Accounts.SingleOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null && replacement is null) throw new InvalidDataException("新增账号需要密码。");
            var next = s.Accounts.Where(a => a != existing).Append((replacement ?? existing!) with { Enabled = enabled }).ToArray();
            if (next.Length > 64 || !next.Any(a => a.Enabled)) throw new InvalidDataException("最多64个账号，且必须保留至少一个可用管理员。");
            Save(s with { Accounts = next, SessionHash = null, SessionAccount = null, SessionExpires = null }, "AdministratorAccountChanged", name);
            // Reset/disable revokes local grants immediately. Other processes recheck the store.
            Revoke();
        }
    }
    public AdministratorDelegation Delegate(string requestHash)
    {
        if (!ExtensionSchema.IsHash(requestHash)) throw new InvalidDataException("维护请求必须使用完整SHA-256身份。");
        if (Remote) return Rpc<AdministratorDelegation>(new("Delegate", RequestHash: requestHash));
        Require();
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); if (!ValidSession(s)) { Revoke(); throw new UnauthorizedAccessException("管理员会话已失效。"); }
            if (s.Delegations?.Length >= 1024) throw new UnauthorizedAccessException("本次会话的维护授权次数已到上限，请重新登录。");
            var grant = new AdministratorDelegation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), account!, requestHash, expires < Now.AddMinutes(2) ? expires : Now.AddMinutes(2), Guid.NewGuid().ToString("N"));
            var record = new Delegation(grant.Id, ProofHash(grant.Token), requestHash, account!, grant.ExpiresAt, s.SessionHash!);
            Save(s with { Delegations = (s.Delegations ?? []).Append(record).ToArray() }, "AdministratorDelegationIssued", account);
            return grant;
        }
    }
    public void ValidateDelegation(AdministratorDelegation grant, string requestHash, bool consume)
    {
        if (Remote) { Rpc<bool>(new("ValidateDelegation", RequestHash: requestHash, Grant: grant, Consume: consume)); return; }
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            var record = s.Delegations?.SingleOrDefault(d => d.Id == grant.Id);
            if (grant.RequestHash != requestHash || grant.ExpiresAt <= Now || s.SessionExpires is not { } sessionExpiry || sessionExpiry <= Now || s.SessionHash is null ||
                record is null || record.RequestHash != requestHash || record.ExpiresAt != grant.ExpiresAt || record.Account != grant.Account || record.SessionHash != s.SessionHash ||
                grant.Token.Length != 64 || s.SessionAccount != grant.Account || !s.Accounts.Any(a => a.Name == grant.Account && a.Enabled) ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(record.TokenHash), System.Text.Encoding.ASCII.GetBytes(ProofHash(grant.Token))))
                throw new UnauthorizedAccessException("软件管理员授权已失效或与本次维护请求不匹配。");
            if (consume)
            {
                if (record.Consumed) throw new UnauthorizedAccessException("维护授权已使用，不能重放。");
                Save(s with { Delegations = s.Delegations!.Select(d => d.Id == grant.Id ? d with { Consumed = true } : d).ToArray() }, "AdministratorDelegationConsumed", grant.Account);
            }
            else if (!record.Consumed) throw new UnauthorizedAccessException("维护授权尚未核销。");
        }
    }
}
