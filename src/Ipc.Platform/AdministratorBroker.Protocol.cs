using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record AdministratorRpcRequest(string Action, string? Token = null, string? Account = null, string? Password = null,
    bool Enabled = true, string? RequestHash = null, AdministratorDelegation? Grant = null, bool Consume = false);
public sealed record AdministratorRpcReply(JsonElement Value, string? Error = null, string? Code = null);
public sealed record AdministratorSessionGrant(string Account, string Token, DateTimeOffset ExpiresAt);

public static partial class AdministratorBroker
{
    internal const int MaximumFrame = 16384;
    internal static async Task<byte[]> ReadFrame(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (size is < 1 or > MaximumFrame) throw new InvalidDataException("认证请求超出长度限制。");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
    }
    internal static async Task WriteFrame(Stream stream, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length is < 1 or > MaximumFrame) throw new InvalidDataException("认证响应超出长度限制。");
        var prefix = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token).ConfigureAwait(false); await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static T Call<T>(string root, AdministratorRpcRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("生产管理员服务需要Windows。");
        using var budget = new CancellationTokenSource(request.Action == "Status" ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", PipeName(root), PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        pipe.ConnectAsync(request.Action == "Status" ? 100 : 2000, budget.Token).GetAwaiter().GetResult();
        RequireServer(root, pipe.SafePipeHandle);
        WriteFrame(pipe, JsonSerializer.SerializeToUtf8Bytes(request), budget.Token).GetAwaiter().GetResult();
        var bytes = ReadFrame(pipe, budget.Token).GetAwaiter().GetResult();
        // DisconnectNamedPipe discards unread bytes. Acknowledge the complete frame
        // before the server reuses an instance, including rejected requests.
        pipe.WriteAsync(new byte[] { 1 }, budget.Token).AsTask().GetAwaiter().GetResult();
        var response = JsonSerializer.Deserialize<AdministratorRpcReply>(bytes) ?? throw new InvalidDataException("认证服务响应无效。");
        if (response.Error is not null)
        {
            if (response.Code == "BootstrapRequiresInstaller") throw new AdministratorBootstrapRequiredException(response.Error);
            if (response.Code == "Unauthorized") throw new UnauthorizedAccessException(response.Error);
            throw new InvalidOperationException(response.Error);
        }
        return response.Value.Deserialize<T>()!;
    }
}

public sealed class AdministratorBootstrapRequiredException(string message) : UnauthorizedAccessException(message);

public sealed partial class LocalAdministrator
{
    bool remoteObserved, knownProvisioned, remoteStatusKnown;
    bool Remote
    {
        get
        {
            if (serviceAuthority || !remoteObserved && !AdministratorBroker.IsManaged(dataRoot)) return false;
            lock (sync) { if (!remoteObserved) { Revoke(); remoteObserved = true; } }
            return true;
        }
    }
    T Rpc<T>(AdministratorRpcRequest request)
    {
        string token; lock (sync) token = sessionSecret;
        return AdministratorBroker.Call<T>(dataRoot, request with { Token = token });
    }
    AdministratorStatus RemoteStatus()
    {
        try
        {
            var status = Rpc<AdministratorStatus>(new("Status"));
            lock (sync) { remoteStatusKnown = true; knownProvisioned = status.Provisioned; if (!status.Authorized) Revoke(); }
            return status;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException)
        { lock (sync) { remoteStatusKnown = true; Revoke(); return new(knownProvisioned, null, null); } }
    }
    bool RemoteLogin(string name, string password)
    {
        var grant = Rpc<AdministratorSessionGrant?>(new("Login", Account: name, Password: password));
        if (grant is null) return false;
        lock (sync)
        {
            Revoke(); revoked = new(); account = grant.Account; sessionSecret = grant.Token; expires = grant.ExpiresAt;
            sessionId = Guid.NewGuid().ToString("N"); knownProvisioned = true;
        }
        return true;
    }
    bool AdoptBrokerToken(string? token)
    {
        if (token is null || token.Length != 64) return false;
        lock (sync)
        {
            using var held = LockStore(); var s = Read(); var credential = s.Accounts.SingleOrDefault(a => a.Enabled && a.Name == s.SessionAccount);
            if (credential is not null && s.SessionHash == ProofHash(token) && s.SessionExpires is { } ended && ended <= Now)
            {
                account = credential.Name;
                try { Save(s with { SessionHash = null, SessionAccount = null, SessionExpires = null, Delegations = [] }, "AdministratorSessionExpired", account); }
                finally { Revoke(); }
                return false;
            }
            if (credential is null || s.SessionExpires is not { } until || until <= Now || s.SessionHash != ProofHash(token)) return false;
            account = credential.Name; credentialHash = credential.Hash; sessionSecret = token; expires = until; sessionId = ProofHash(token); return true;
        }
    }
    internal object? HandleBrokerRequest(AdministratorRpcRequest request, bool installer)
    {
        if (!serviceAuthority) throw new UnauthorizedAccessException("仅本机受保护认证服务可以处理此请求。");
        var authenticated = AdoptBrokerToken(request.Token);
        switch (request.Action)
        {
            case "Status":
                var status = Status(); return authenticated ? status : status with { Account = null, ExpiresAt = null };
            case "Provision":
                if (!installer) throw new AdministratorBootstrapRequiredException("首次开通须由Windows安装管理员批准；软件密码仍由本次输入决定。");
                Provision(request.Account ?? "", request.Password ?? ""); return true;
            case "Login":
                if (!Login(request.Account ?? "", request.Password ?? "")) return null;
                return new AdministratorSessionGrant(account!, sessionSecret, expires);
            case "ValidateDelegation":
                ValidateDelegation(request.Grant ?? throw new UnauthorizedAccessException("维护授权缺失。"), request.RequestHash ?? "", request.Consume); return true;
            case "Logout":
                if (authenticated) Logout(); return true;
        }
        if (!authenticated) throw new UnauthorizedAccessException("请先登录软件管理员；会话已撤销或到期。");
        switch (request.Action)
        {
            case "Accounts": return Accounts();
            case "Maintain": Maintain(request.Account ?? "", request.Password, request.Enabled); return true;
            case "Delegate": return Delegate(request.RequestHash ?? "");
            default: throw new InvalidDataException("不支持的认证操作。");
        }
    }
}
