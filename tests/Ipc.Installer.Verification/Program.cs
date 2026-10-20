using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ipc.Installer;

var source = new string('a', 40);
var checks = 0;
void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; Console.WriteLine("PASS " + reason); }
var workspace = Path.Combine(Path.GetTempPath(), "ipc-installer-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
byte[] Zip(Dictionary<string, byte[]> files, string? extra = null, int attributes = 0)
{
    using var output = new MemoryStream();
    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
    {
        foreach (var (name, content) in files)
        {
            var entry = zip.CreateEntry(name); using var stream = entry.Open(); stream.Write(content);
        }
        if (extra is not null) { var entry = zip.CreateEntry(extra); entry.ExternalAttributes = attributes; using var stream = entry.Open(); stream.WriteByte(1); }
    }
    return output.ToArray();
}
PayloadDescriptor Descriptor(byte[] data)
{
    using var zip = new ZipArchive(new MemoryStream(data));
    return new("sportmaster", source, "test", Convert.ToHexString(SHA256.HashData(data)), data.Length, zip.Entries.Sum(e => e.Length), zip.Entries.Count);
}
string NextRoot() => Path.Combine(workspace, Guid.NewGuid().ToString("N"));
async Task Reject(byte[] data, PayloadDescriptor descriptor, string reason, CancellationToken token = default)
{
    var rejected = false;
    try { await InstallerPayload.PrepareAsync(new MemoryStream(data), descriptor, NextRoot(), null, token); }
    catch (Exception error) when (error is InvalidDataException or IOException or OperationCanceledException) { rejected = true; }
    Check(rejected, reason);
}
try
{
    var release = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ApplicationId = "sportmaster", SourceCommit = source }));
    var files = new Dictionary<string, byte[]> { ["app/application-release.json"] = release, ["desktop/application-release.json"] = release, ["desktop/Ipc.Desktop.exe"] = [1, 2, 3], ["config/中文.json"] = Encoding.UTF8.GetBytes("合成配置") };
    files["SHA256SUMS"] = Encoding.UTF8.GetBytes(string.Join("", files.Select(p => Convert.ToHexString(SHA256.HashData(p.Value)) + "  " + p.Key + "\n")));
    var valid = Zip(files); var descriptor = Descriptor(valid); var target = NextRoot();
    var progress = new List<PreparationProgress>();
    var executable = await InstallerPayload.PrepareAsync(new MemoryStream(valid), descriptor, target, new ImmediateProgress(value => progress.Add(value)), default);
    Check(File.ReadAllBytes(executable).SequenceEqual(new byte[] { 1, 2, 3 }) && File.ReadAllText(Path.Combine(target, "config/中文.json")) == "合成配置", "complete payload and Unicode paths retain exact bytes");
    Check(progress.First().Percent >= 0 && progress.Last().Percent == 100 && progress.Zip(progress.Skip(1)).All(p => p.First.Percent <= p.Second.Percent), "real byte progress is monotonic and reaches verified handoff");
    var refusedOverwrite = false;
    try { await InstallerPayload.PrepareAsync(new MemoryStream(valid), descriptor, target, null, default); } catch (IOException) { refusedOverwrite = true; }
    Check(refusedOverwrite && File.ReadAllBytes(executable).SequenceEqual(new byte[] { 1, 2, 3 }), "existing media is never overwritten");
    await Reject(valid, descriptor with { Sha256 = new string('0', 64) }, "aggregate byte tampering refuses preparation");
    await Reject(valid, descriptor with { UnpackedBytes = descriptor.UnpackedBytes - 1 }, "unpacked size bound rejects inconsistent payload");
    await Reject(valid, descriptor with { FileCount = descriptor.FileCount + 1 }, "file count mismatch fails closed");
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel(); await Reject(valid, descriptor, "cancellation prevents completed handoff", cancellation.Token);
    }
    using (var cancellation = new CancellationTokenSource())
    {
        var partial = NextRoot(); var cancelled = false;
        try { await InstallerPayload.PrepareAsync(new MemoryStream(valid), descriptor, partial, new ImmediateProgress(value => { if (value.Percent >= 15) cancellation.Cancel(); }), cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && !File.Exists(Path.Combine(partial, "desktop/Ipc.Desktop.exe")), "cancellation during verification cannot launch an unverified executable");
    }
    foreach (var name in new[] { "../escape", "/absolute", "C:/drive", "a\\escape", "a/../escape", "a//file", "a/file.", "a/file ", "CON.txt", "AUX/file", "a/evil:stream", "a/COM1" })
    {
        var bad = Zip(files, name); await Reject(bad, Descriptor(bad), "unsafe path rejected: " + name);
    }
    var duplicate = Zip(files, "DESKTOP/ipc.desktop.EXE"); await Reject(duplicate, Descriptor(duplicate), "Windows case-insensitive duplicate paths rejected");
    var link = Zip(files, "link", 0xA000 << 16); await Reject(link, Descriptor(link), "symbolic-link archive entry rejected");
    var missing = new Dictionary<string, byte[]>(files); missing.Remove("desktop/Ipc.Desktop.exe");
    var missingZip = Zip(missing); await Reject(missingZip, Descriptor(missingZip), "missing inventoried executable rejected");
    var unlisted = Zip(files, "unlisted.dll"); await Reject(unlisted, Descriptor(unlisted), "unlisted program file rejected");
    var changed = new Dictionary<string, byte[]>(files) { ["desktop/Ipc.Desktop.exe"] = [9] };
    var changedZip = Zip(changed); await Reject(changedZip, Descriptor(changedZip), "per-file mismatch rejected even with matching aggregate descriptor");
    await Reject(valid, descriptor with { SourceCommit = new string('b', 40) }, "mixed source identity rejected");
    Check(!File.Exists(Path.Combine(workspace, "escape")), "rejected archive entries do not escape the preparation root");
    Console.WriteLine($"PASS {checks} installer preparation checks; no executable launched or machine policy modified");
}
finally { Directory.Delete(workspace, true); }

sealed class ImmediateProgress(Action<PreparationProgress> report) : IProgress<PreparationProgress>
{
    public void Report(PreparationProgress value) => report(value);
}
