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
public sealed class LocalAdministrator
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LocalAdministrator> stores = new(StringComparer.OrdinalIgnoreCase);
    public static LocalAdministrator ForRoot(string root) => stores.GetOrAdd(Path.GetFullPath(root), r => new(r));
    readonly string directory, path;
    readonly object sync = new();
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
    sealed record Store(Credential[] Accounts, int Failures, DateTimeOffset LockedUntil, Audit[] Audit,
        string? SessionHash = null, string? SessionAccount = null, DateTimeOffset? SessionExpires = null, string[]? UsedDelegations = null);
    static string ProofHash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public LocalAdministrator(string root)
    { directory = Path.Combine(Path.GetFullPath(root), "security"); path = Path.Combine(directory, "administrators.json"); }
    FileStream LockStore()
    {
        DeploymentPaths.RequireOrdinaryAncestors(path); Directory.CreateDirectory(directory);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    Store Read()
    {
        if (!File.Exists(path)) return new([], 0, default, []);
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
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, s); stream.Flush(true); }
            File.Move(temp, path, true);
            RuntimeJournal.AppendMetadata(Path.Combine(directory, "diagnostics"), new { At = DateTimeOffset.UtcNow, Source = "认证", Level = action == "AdministratorLoginFailed" ? "警告" : "信息", Event = action, Actor = account, TargetAccount = target });
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public AdministratorStatus Status()
    {
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (account is not null && (Now >= expires || s.SessionHash != ProofHash(sessionSecret) || !s.Accounts.Any(a => a.Enabled && a.Name == account && a.Hash == credentialHash))) Revoke();
            return new(s.Accounts.Length > 0, account, account is null ? null : expires);
        }
    }
    public OperatorIdentity? Identity
    {
        get { var s = Status(); return s.Authorized ? new(s.Account!, s.Account!, "software-administrator", sessionId, expires.AddMinutes(-15), false, expires, ["maintenance"]) : null; }
    }
    void Revoke() { account = null; revoked.Cancel(); }
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
        var credential = Create(name, password);
        lock (sync) { using var held = LockStore(); var s = Read(); if (s.Accounts.Length != 0) throw new UnauthorizedAccessException("已开通管理员，请使用登录；不能覆盖原账号。"); Save(s with { Accounts = [credential] }, "AdministratorProvisioned", name); }
    }
    public bool Login(string name, string password)
    {
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
            Save(s with { Failures = 0, LockedUntil = default, SessionHash = ProofHash(secret), SessionAccount = found!.Name, SessionExpires = now.AddMinutes(15), UsedDelegations = [] }, "AdministratorLoggedIn", found!.Name);
            Revoke(); revoked = new(); account = found!.Name; credentialHash = found.Hash; sessionId = Guid.NewGuid().ToString("N"); sessionSecret = secret; expires = now.AddMinutes(15); revoked.CancelAfter(TimeSpan.FromMinutes(15)); return true;
        }
    }
    public void Logout()
    { lock (sync) { try { using var held = LockStore(); var s = Read(); Save(s with { SessionHash = null, SessionAccount = null, SessionExpires = null }, "AdministratorLoggedOut", account); } finally { Revoke(); } } }
    public AdministratorAccount[] Accounts()
    { Require(); lock (sync) { using var held = LockStore(); return Read().Accounts.Select(a => new AdministratorAccount(a.Name, a.Enabled)).ToArray(); } }
    public void Maintain(string name, string? password, bool enabled)
    {
        Require(); var replacement = password is null ? null : Create(name, password);
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (Now >= expires || account is null || !s.Accounts.Any(a => a.Enabled && a.Name == account && a.Hash == credentialHash)) throw new UnauthorizedAccessException("管理员会话已过期。");
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
        lock (sync) { Require(); return new(sessionSecret, account!, requestHash, expires < Now.AddMinutes(2) ? expires : Now.AddMinutes(2), Guid.NewGuid().ToString("N")); }
    }
    public void ValidateDelegation(AdministratorDelegation grant, string requestHash, bool consume)
    {
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (grant.RequestHash != requestHash || grant.ExpiresAt <= Now || s.SessionExpires is not { } sessionExpiry || sessionExpiry <= Now || s.SessionHash is null ||
                grant.Token.Length != 64 || s.SessionAccount != grant.Account || !s.Accounts.Any(a => a.Name == grant.Account && a.Enabled) ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(s.SessionHash), System.Text.Encoding.ASCII.GetBytes(ProofHash(grant.Token))))
                throw new UnauthorizedAccessException("软件管理员授权已失效或与本次维护请求不匹配。");
            if (consume)
            {
                if (s.UsedDelegations?.Length >= 1024) throw new UnauthorizedAccessException("本次会话的维护授权次数已到上限，请重新登录。");
                if (s.UsedDelegations?.Contains(grant.Id) == true) throw new UnauthorizedAccessException("维护授权已使用，不能重放。");
                Save(s with { UsedDelegations = (s.UsedDelegations ?? []).Append(grant.Id).ToArray() }, "AdministratorDelegationConsumed", grant.Account);
            }
        }
    }
}
