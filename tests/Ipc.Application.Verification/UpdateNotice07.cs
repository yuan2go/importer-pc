using System.Text.Json;
using Ipc.Platform;

// IE-P07 optional update notice: real files in a temporary update folder, no network, no installer.
internal static class UpdateNotice07
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS UPDATE07 " + text); }
    const string Product = "importer-pc-sportmaster";
    internal static void Run(string root)
    {
        var folder = Path.Combine(root, "update-notice-07", "updates"); Directory.CreateDirectory(folder);
        var share = Path.Combine(root, "update-notice-07", "share"); Directory.CreateDirectory(share);
        var hash = new string('a', 64);
        void Note(string directory, object value) => File.WriteAllText(Path.Combine(directory, UpdateNotices.ReleaseFile), JsonSerializer.Serialize(value));
        void Clear() { foreach (var f in Directory.GetFiles(folder)) File.Delete(f); }
        UpdateNotice Read(string current = "1.0.2") => UpdateNotices.Read(folder, Product, current, TimeSpan.FromSeconds(3));

        Check(Read().State == UpdateNoticeState.None, "no release note reads as nothing configured");

        Note(folder, new { Product, Version = "1.0.3", PublishedAt = "2026-11-10", Summary = "修复相机重连", Media = @"\\server\share\Setup-1.0.3.exe", Sha256 = hash });
        var available = Read();
        Check(available.State == UpdateNoticeState.Available && available.Text.Contains("1.0.3") && available.Text.Contains("修复相机重连") &&
            available.Text.Contains(@"\\server\share\Setup-1.0.3.exe") && available.Text.Contains("不会自动更新"), "a newer stable release is announced with its media and hash, without installing");
        Check(Read("1.0.3").State == UpdateNoticeState.Current && Read("1.1.0").State == UpdateNoticeState.Current, "the same or an older release reads as up to date");
        Check(Read("1.0.10").State == UpdateNoticeState.Current, "versions compare by number, so 1.0.3 is not newer than 1.0.10");

        Note(folder, new { Product, Version = "1.0.3-rc.1" });
        Check(Read("1.0.2").State == UpdateNoticeState.Current && Read("1.0.3-beta.2").State == UpdateNoticeState.Available,
            "a prerelease is offered only to a station already on a prerelease");

        Note(folder, new { Product, Version = "1.0.3", Revoked = new[] { "1.0.2" } });
        var revoked = Read();
        Check(revoked.State == UpdateNoticeState.CurrentRevoked && revoked.Text.Contains("已撤回"), "a revoked current version is called out");

        Note(folder, new { Product = "importer-pc-direct-plc", Version = "9.9.9" });
        Check(Read().State == UpdateNoticeState.Unreadable, "a release note for another product is ignored");
        File.WriteAllText(Path.Combine(folder, UpdateNotices.ReleaseFile), "{ not json");
        Check(Read().State == UpdateNoticeState.Unreadable, "a malformed note is ignored without an exception");
        Note(folder, new { Product, Version = "1.03" });
        Check(Read().State == UpdateNoticeState.Unreadable, "an invalid version is ignored");
        Note(folder, new { Product, Version = "1.0.3", Sha256 = "xyz" });
        Check(Read().State == UpdateNoticeState.Unreadable, "a hash that is not SHA-256 is rejected");
        File.WriteAllText(Path.Combine(folder, UpdateNotices.ReleaseFile), new string(' ', 70 * 1024) + "{}");
        Check(Read().State == UpdateNoticeState.Unreadable, "an oversized note is rejected before parsing");

        Clear();
        Note(share, new { Product, Version = "1.0.4" });
        File.WriteAllText(Path.Combine(folder, UpdateNotices.PointerFile), Path.Combine(share, UpdateNotices.ReleaseFile) + Environment.NewLine);
        var pointed = Read();
        Check(pointed.State == UpdateNoticeState.Available && pointed.Source == Path.Combine(share, UpdateNotices.ReleaseFile), "source.txt points the station at a shared release note");
        File.WriteAllText(Path.Combine(folder, UpdateNotices.PointerFile), "relative/release.json");
        Check(Read().State == UpdateNoticeState.Unreadable, "a relative pointer is refused");

        File.WriteAllText(Path.Combine(folder, UpdateNotices.PointerFile), Path.Combine(share, UpdateNotices.ReleaseFile));
        var hang = new ManualResetEventSlim();
        var started = DateTime.UtcNow;
        var slow = UpdateNotices.Read(folder, Product, "1.0.2", TimeSpan.FromMilliseconds(300), _ => { hang.Wait(); return new(Product, "1.0.4"); });
        hang.Set();
        Check(slow.State == UpdateNoticeState.Unreadable && slow.Text.Contains("超时") && DateTime.UtcNow - started < TimeSpan.FromSeconds(3),
            "a share that does not answer times out quickly and reads as unavailable");
    }
}
