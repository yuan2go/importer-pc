using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ipc.Installer;

public sealed record PayloadDescriptor(string ApplicationId, string SourceCommit, string BusinessVersion, string Sha256, long ZipBytes, long UnpackedBytes, int FileCount);
public sealed record PreparationProgress(string Stage, string File, double Percent);

public static class InstallerPayload
{
    public static string SafeRelativePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.StartsWith('/') || name.Any(c => c < 32 || "<>:\"|?*".Contains(c)))
            throw new InvalidDataException("安装包包含非法路径：" + name);
        foreach (var part in name.Split('/'))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
                throw new InvalidDataException("安装包包含非法路径：" + name);
        }
        return name;
    }

    public static void RequireOrdinaryAncestors(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装准备目录不能经过链接：" + item.FullName);
    }

    public static async Task<string> PrepareAsync(Stream input, PayloadDescriptor descriptor, string target, IProgress<PreparationProgress>? progress, CancellationToken cancellationToken)
    {
        if (!input.CanSeek || input.Length != descriptor.ZipBytes || descriptor.ZipBytes <= 0 || descriptor.UnpackedBytes <= 0 || descriptor.FileCount <= 0 ||
            descriptor.SourceCommit.Length != 40 || !descriptor.SourceCommit.All(Uri.IsHexDigit) || descriptor.Sha256.Length != 64 || !descriptor.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("内嵌安装包说明无效");
        target = Path.GetFullPath(target);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("安装准备目录已经存在，禁止覆盖");
        RequireOrdinaryAncestors(target);
        input.Position = 0;
        var buffer = new byte[131072];
        long read = 0;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
            {
                hash.AppendData(buffer, 0, count); read += count;
                progress?.Report(new("正在校验安装文件", "", 15d * read / descriptor.ZipBytes));
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装文件已变化或损坏；请重新取得完整安装程序");
        }
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            SafeRelativePath(entry.FullName);
            if (!names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("安装包包含重复文件或链接：" + entry.FullName);
            total = checked(total + entry.Length);
        }
        if (total != descriptor.UnpackedBytes || archive.Entries.Count != descriptor.FileCount)
            throw new InvalidDataException("安装包文件数量或展开大小不符");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(target);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long written = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.GetFullPath(Path.Combine(target, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装路径越界");
            RequireOrdinaryAncestors(Path.GetDirectoryName(file)!);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long fileBytes = 0;
            await using (var source = entry.Open())
            await using (var destination = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    fileBytes = checked(fileBytes + count); written = checked(written + count);
                    if (fileBytes > entry.Length || written > descriptor.UnpackedBytes) throw new InvalidDataException("安装包展开超出限定大小");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    hash.AppendData(buffer, 0, count);
                }
            }
            if (fileBytes != entry.Length) throw new InvalidDataException("安装文件长度不符：" + entry.FullName);
            hashes.Add(entry.FullName, Convert.ToHexString(hash.GetHashAndReset()));
            progress?.Report(new("正在准备安装文件", entry.FullName, 15 + 80d * written / total));
        }
        progress?.Report(new("正在复核安装文件", "", 95));
        var inventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(target, "SHA256SUMS"), cancellationToken))
        {
            if (line.Length < 67 || line[64..66] != "  " || !line[..64].All(Uri.IsHexDigit)) throw new InvalidDataException("安装文件清单无效");
            var name = SafeRelativePath(line[66..]);
            if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) || !inventory.Add(name) || !hashes.TryGetValue(name, out var actual) || !actual.Equals(line[..64], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装文件校验失败：" + name);
        }
        if (inventory.Count + 1 != hashes.Count) throw new InvalidDataException("安装包存在清单外文件");
        foreach (var area in new[] { "app", "desktop" })
        {
            using var release = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, area, "application-release.json"), cancellationToken));
            if (release.RootElement.GetProperty("SourceCommit").GetString() != descriptor.SourceCommit || release.RootElement.GetProperty("ApplicationId").GetString() != descriptor.ApplicationId || release.RootElement.GetProperty("BusinessVersion").GetString() != descriptor.BusinessVersion)
                throw new InvalidDataException("安装包的程序版本不一致");
        }
        var executable = Path.Combine(target, "desktop", "Ipc.Desktop.exe");
        if (!File.Exists(executable)) throw new InvalidDataException("安装向导程序缺失");
        progress?.Report(new("安装文件已就绪", "", 100));
        return executable;
    }
}
