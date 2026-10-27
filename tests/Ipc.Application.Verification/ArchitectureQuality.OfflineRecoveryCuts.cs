using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class ArchitectureQuality
{
    public static async Task CurrentOfflineRecoveryChild(string database, string occurrence, string cut, string marker)
    {
        typeof(ParcelOfflineReceiptRecovery).GetField("Boundary", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, (Action<string>)(point => { if (point == cut) Environment.Exit(76); }));
        await ParcelOfflineReceiptRecovery.RecoverAsync(database, Wire.FileHash(database), marker + ".backup.db", occurrence,
            Path.GetDirectoryName(typeof(ArchitectureQuality).Assembly.Location)!, "Synthetic current receipt process cut", new SportmasterFlow(),
            async (json, _, _, token) =>
            {
                var request = Wire.Decode<ParcelOperation>(json).Upload!.Request;
                File.AppendAllText(marker, request.Id + " " + Wire.Hash(Wire.Encode(request)) + Environment.NewLine);
                await using var uploader = new Ipc.Components.ReferenceImageUploader(request.Binding, request.Binding.Parameters["peer-root"]);
                await uploader.StartAsync(token); return await uploader.QueryAsync(request, token);
            }, expectedWalHash: WalHash(database));
        throw new InvalidOperationException("Required offline recovery boundary was not reached: " + cut);
    }

    static async Task CurrentOfflineRecoveryCuts(ParcelConfiguration configuration)
    {
        foreach (var cut in new[] { "AfterReceiptRawDurable", "AfterReceiptOutcomeCommit" })
        {
            var directory = DirectoryFor("current-offline-" + cut, configuration);
            ParcelRecovery.Restore(Path.Combine(root, "legacy-before.backup.db"), directory);
            var database = Path.Combine(directory, "sportmaster.db"); string occurrence; ParcelOperation original;
            using (var repository = new ParcelRepository(directory, "sportmaster"))
            {
                original = repository.Load().Single(x => x.AdmissionId == "interrupted-attached");
                occurrence = repository.PendingReceipts().Single(x => x.Operation == original.Id && x.Stage == "image-upload" && x.Reply is null).Id;
            }
            var marker = Path.Combine(root, cut + ".queries");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { typeof(ArchitectureQuality).Assembly.Location, workspace, "--current-offline-recovery-child", database, occurrence, cut, marker }) start.ArgumentList.Add(arg);
            using (var child = Process.Start(start)!)
            {
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
                catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
                Check(child.ExitCode == 76, cut + " real process terminates at the intended current receipt boundary: " + await output + await error);
            }
            var calls = 0;
            await ParcelOfflineReceiptRecovery.RecoverAsync(database, Wire.FileHash(database), marker + ".restart.backup.db", occurrence,
                Path.GetDirectoryName(typeof(ArchitectureQuality).Assembly.Location)!, "Synthetic local completion after current process interruption", new SportmasterFlow(),
                (_, _, _, _) => { calls++; return Task.FromResult<StageReply?>(null); }, expectedWalHash: WalHash(database));
            using var readback = new ParcelRepository(directory, "sportmaster"); var current = readback.Load().Single(x => x.Id == original.Id);
            Check(calls == 0 && File.ReadAllLines(marker).Length == 1 && readback.HasReceiptOutcomeBasis(occurrence) && readback.MissingAwaitingRaw(occurrence) && !current.ImageReleased &&
                Wire.Encode(current.Wcs) == Wire.Encode(original.Wcs) && Wire.Encode(current.Upload!.Request) == Wire.Encode(original.Upload!.Request) && current.Upload.Attempts == original.Upload.Attempts,
                cut + " restart completes durable Query locally, retains missing original raw/image responsibility and never repeats Query or Submit");
        }
    }
}
