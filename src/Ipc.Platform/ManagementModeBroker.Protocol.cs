using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record ManagementModeRpcRequest(string Action, string? Token = null, string? LegacyAccount = null, string? Password = null,
    string? NewPassword = null, string? RequestHash = null, ManagementModeDelegation? Grant = null, bool Consume = false);
public sealed record ManagementModeRpcReply(JsonElement Value, string? Error = null, string? Code = null);
public sealed record ManagementModeSessionGrant(string SessionId, string Token, DateTimeOffset ExpiresAt);

public static partial class ManagementModeBroker
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
    internal static T Call<T>(string root, ManagementModeRpcRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("管理模式授权服务需要Windows。");
        using var budget = new CancellationTokenSource(request.Action == "Status" ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", PipeName(root), PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        pipe.ConnectAsync(request.Action == "Status" ? 100 : 2000, budget.Token).GetAwaiter().GetResult();
        RequireServer(root, pipe.SafePipeHandle);
        WriteFrame(pipe, JsonSerializer.SerializeToUtf8Bytes(request), budget.Token).GetAwaiter().GetResult();
        var bytes = ReadFrame(pipe, budget.Token).GetAwaiter().GetResult();
        // DisconnectNamedPipe discards unread bytes. Acknowledge the complete frame
        // before the server reuses an instance, including rejected requests.
        pipe.WriteAsync(new byte[] { 1 }, budget.Token).AsTask().GetAwaiter().GetResult();
        var response = JsonSerializer.Deserialize<ManagementModeRpcReply>(bytes) ?? throw new InvalidDataException("认证服务响应无效。");
        if (response.Error is not null)
        {
            if (response.Code == "BootstrapRequiresInstaller") throw new ManagementModeBootstrapRequiredException(response.Error);
            if (response.Code == "Unauthorized") throw new UnauthorizedAccessException(response.Error);
            throw new InvalidOperationException(response.Error);
        }
        return response.Value.Deserialize<T>()!;
    }
}

public sealed class ManagementModeBootstrapRequiredException(string message) : UnauthorizedAccessException(message);

public sealed partial class LocalManagementMode
{
    bool remoteObserved, knownConfigured, remoteStatusKnown, knownLegacy, knownAvailable = true;
    bool Remote
    {
        get
        {
            if (serviceAuthority || !remoteObserved && !ManagementModeBroker.IsManaged(dataRoot)) return false;
            lock (sync) { if (!remoteObserved) { Revoke(); remoteObserved = true; } }
            return true;
        }
    }
    T Rpc<T>(ManagementModeRpcRequest request)
    {
        string token; lock (sync) token = sessionSecret;
        return ManagementModeBroker.Call<T>(dataRoot, request with { Token = token });
    }
    ManagementModeStatus RemoteStatus()
    {
        try
        {
            var status = Rpc<ManagementModeStatus>(new("Status"));
            lock (sync) { remoteStatusKnown = true; knownConfigured = status.Configured; knownLegacy = status.NeedsLegacyConversion; knownAvailable = status.Available; if (!status.Authorized) Revoke(); }
            return status;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { lock (sync) { remoteStatusKnown = true; knownAvailable = false; Revoke(); return new(knownConfigured, false, null, knownLegacy, false); } }
    }
    bool RemoteEnter(string password)
    {
        var grant = Rpc<ManagementModeSessionGrant?>(new("Enter", Password: password));
        if (grant is null) return false;
        lock (sync)
        {
            Revoke(); revoked = new(); active = true; sessionSecret = grant.Token; expires = grant.ExpiresAt;
            if (expires > Now) revoked.CancelAfter(expires - Now);
            sessionId = grant.SessionId; knownConfigured = true; knownLegacy = false; knownAvailable = true;
        }
        return true;
    }
    bool AdoptBrokerToken(string? token)
    {
        if (token is null || token.Length != 64) return false;
        lock (sync)
        {
            using var held = LockStore(); var s = Read();
            if (s.Credential is not null && s.SessionHash == ProofHash(token) && s.SessionExpires is { } ended && ended <= Now)
            {
                active = true; sessionId = s.SessionId ?? "";
                try { Save(s with { SessionHash = null, SessionId = null, SessionExpires = null, Delegations = [] }, "ManagementModeExpired"); }
                finally { Revoke(); }
                return false;
            }
            if (s.Credential is null || s.SessionId is null || s.SessionExpires is not { } until || until <= Now || s.SessionHash != ProofHash(token)) return false;
            active = true; sessionId = s.SessionId; credentialHash = s.Credential.Hash; sessionSecret = token; expires = until; return true;
        }
    }
    internal object? HandleBrokerRequest(ManagementModeRpcRequest request, bool installer, string? windowsSid = null)
    {
        if (!serviceAuthority) throw new UnauthorizedAccessException("仅受保护管理模式服务可以处理此请求。");
        callerWindowsSid = windowsSid;
        var authenticated = AdoptBrokerToken(request.Token);
        switch (request.Action)
        {
            case "Status":
                var status = Status(); return authenticated ? status : status with { Active = false, ExpiresAt = null };
            case "ConfigurePassword":
                if (!installer) throw new ManagementModeBootstrapRequiredException("首次设置管理密码须由Windows安装维护批准；进入管理模式仍须验证本次设置的密码。");
                ConfigurePassword(request.Password ?? ""); return true;
            case "Enter":
                if (!Enter(request.Password ?? "")) return null;
                return new ManagementModeSessionGrant(sessionId, sessionSecret, expires);
            case "ConvertLegacy": return ConvertLegacyCredentials(request.LegacyAccount ?? "", request.Password ?? "", request.NewPassword ?? "");
            case "ValidateDelegation":
                ValidateDelegation(request.Grant ?? throw new UnauthorizedAccessException("维护授权缺失。"), request.RequestHash ?? "", request.Consume); return true;
            case "Exit": if (authenticated) Exit(); return true;
        }
        if (!authenticated) throw new UnauthorizedAccessException("请先进入管理模式；原授权已撤销或到期。");
        switch (request.Action)
        {
            case "ChangePassword": ChangePassword(request.Password ?? ""); return true;
            case "Delegate": return Delegate(request.RequestHash ?? "");
            default: throw new InvalidDataException("不支持的管理模式操作。");
        }
    }
}
