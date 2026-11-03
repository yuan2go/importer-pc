using System.Text.Json;

namespace Ipc.Platform;

// Optional, offline update notice. An implementer places a release note in the station's
// update folder, or a one-line pointer to a release note on a local share. The client only
// reads and compares versions: it never downloads, installs, restarts or blocks work, and a
// missing, slow or malformed note only changes what the maintenance page says.
public sealed record UpdateRelease(string Product, string Version, string? PublishedAt = null, string? Summary = null,
    string? Media = null, string? Sha256 = null, string[]? Revoked = null);
public enum UpdateNoticeState { None, Current, Available, CurrentRevoked, Unreadable }
public sealed record UpdateNotice(UpdateNoticeState State, string Text, UpdateRelease? Release, string? Source);

public static class UpdateNotices
{
    public const string FolderName = "updates";
    public const string ReleaseFile = "release.json";
    public const string PointerFile = "source.txt";
    const int MaximumBytes = 64 * 1024;
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 };

    // folder: <data root>\updates. A share that does not answer within the timeout reads as
    // unreadable; the abandoned read cannot change anything because nothing acts on it.
    public static UpdateNotice Read(string folder, string product, string currentVersion, TimeSpan timeout) =>
        Read(folder, product, currentVersion, timeout, ReadRelease);
    // readRelease is the file read; tests substitute a share that does not answer.
    public static UpdateNotice Read(string folder, string product, string currentVersion, TimeSpan timeout, Func<string, UpdateRelease> readRelease)
    {
        string? source = null;
        try
        {
            var local = Path.Combine(folder, ReleaseFile);
            var pointer = Path.Combine(folder, PointerFile);
            source = File.Exists(local) ? local : File.Exists(pointer) ? PointerTarget(pointer) : null;
            if (source is null) return new(UpdateNoticeState.None, "未配置新版本说明。", null, null);
            var read = Task.Run(() => readRelease(source));
            if (!read.Wait(timeout)) return new(UpdateNoticeState.Unreadable, "新版本说明读取超时，请检查共享目录是否可访问。", null, source);
            var release = read.Result;
            if (!string.Equals(release.Product, product, StringComparison.Ordinal))
                return new(UpdateNoticeState.Unreadable, "新版本说明不是本软件的，已忽略。", null, source);
            if (release.Revoked?.Contains(currentVersion, StringComparer.Ordinal) == true)
                return new(UpdateNoticeState.CurrentRevoked, $"当前版本 {currentVersion} 已撤回，请联系实施人员尽快安装 {release.Version}。" + MediaText(release), release, source);
            var newer = ReleaseVersionPolicy.Compare(release.Version, currentVersion) > 0;
            // A prerelease is offered only to stations already running a prerelease.
            var offered = newer && (!release.Version.Contains('-') || currentVersion.Contains('-'));
            return offered
                ? new(UpdateNoticeState.Available, $"有新版本 {release.Version}" + (release.PublishedAt is { Length: > 0 } at ? $"（{at}）" : "") +
                    (release.Summary is { Length: > 0 } summary ? "：" + summary : "") + "。由实施人员按部署卡安装，软件不会自动更新。" + MediaText(release), release, source)
                : new(UpdateNoticeState.Current, $"当前版本 {currentVersion} 已是最新。", release, source);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or AggregateException)
        {
            return new(UpdateNoticeState.Unreadable, "新版本说明无法读取或格式不对，已忽略。", null, source);
        }
    }
    static string PointerTarget(string pointer)
    {
        var target = File.ReadLines(pointer).FirstOrDefault()?.Trim() ?? "";
        if (target.Length is 0 or > 1024 || !Path.IsPathFullyQualified(target)) throw new InvalidDataException("Update pointer must hold one absolute path");
        return target;
    }
    public static UpdateRelease ReadRelease(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!stream.CanSeek || stream.Length > MaximumBytes) throw new InvalidDataException("Release note too large");
        var release = JsonSerializer.Deserialize<UpdateRelease>(stream, Options) ?? throw new InvalidDataException("Empty release note");
        if (string.IsNullOrWhiteSpace(release.Product)) throw new InvalidDataException("Release note names no product");
        ReleaseVersionPolicy.Compare(release.Version, release.Version); // validates the version format
        if (release.Sha256 is { } hash && (hash.Length != 64 || !hash.All(Uri.IsHexDigit))) throw new InvalidDataException("Release note hash is not SHA-256");
        if (release.Summary is { Length: > 200 } || release.Media is { Length: > 1024 }) throw new InvalidDataException("Release note text too long");
        return release;
    }
    static string MediaText(UpdateRelease release) =>
        release.Media is { Length: > 0 } media ? $"\n安装介质：{media}" + (release.Sha256 is { } hash ? $"\nSHA-256：{hash}" : "") : "";
}
