using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    sealed class OriginalDecoder : IMonoBarcodeDecoder
    {
        public readonly ConcurrentQueue<string[]> Results = new();
        public void Prepare() { }
        public string[] Decode(byte[] pixels, int length, int width, int height)
        { Array.Fill(pixels, (byte)0); return Results.TryDequeue(out var codes) ? codes : []; }
        public void Release() { }
    }
    public static async Task OriginalFrameChecks(string workspace, string root)
    {
        Check(Wire.Encode(new BarcodeFilterPolicy("A.*")) == Wire.Encode(new { Pattern = "A.*" }), "default new options preserve original frozen policy bytes and hash");
        Check(BarcodeFiltering.Select(null, ["A", "A"]).Outcome == "MultipleCodes" &&
            BarcodeFiltering.Select(new(""), ["A", "B"]).Outcome == "AmbiguousCodes", "default strict multi-code refusal remains unchanged");
        Check(BarcodeFiltering.Select(new("", MultiCodeSeparator: "|"), ["A", "bad\0code"]).SelectedCode is null,
            "editing an inactive separator alone cannot relax the original strict no-filter guard");
        var joined = BarcodeFiltering.Select(new("", RejectMultipleCodes: false, MultiCodeSeparator: "|"), ["B", "A", "A"]);
        Check(joined.SelectedCode == "B|A|A" && joined.Matches == 3 && joined.Outcome == "JoinedCodes", "explicit join preserves SDK order and duplicate multiplicity");
        Check(BarcodeFiltering.Select(new("A.*", "AX", false, ";"), ["B", "A1", "AX", "A2"]).SelectedCode == "A1;A2", "allow/exclusion filters run on each candidate before joining");
        Check(BarcodeFiltering.Validate(new("", RejectMultipleCodes: false, MultiCodeSeparator: "")) is not null &&
            BarcodeFiltering.Validate(new("", RejectMultipleCodes: false, MultiCodeSeparator: "\n")) is not null &&
            BarcodeFiltering.Select(new("", RejectMultipleCodes: false), [new string('A', 256), new string('B', 256)]).SelectedCode is null &&
            BarcodeFiltering.Select(new("", RejectMultipleCodes: false), ["A", "B"], "Incomplete").SelectedCode is null,
            "invalid delimiter, overlong joined payload and incomplete candidates refuse without truncation");
        await using var peer = new Peer();
        var seed = HeartbeatConfiguration(workspace, Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" });
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-mvs-reader", Parameters = new()
        { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-ORIGINAL-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" } }).ToArray();
        var config = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Login = null,
            DeferredConnections = [readers[1].DeviceId], BarcodeFilter = new("", RejectMultipleCodes: false, MultiCodeSeparator: "|") };
        var configRoot = Path.Combine(root, "original-config"); Directory.CreateDirectory(configRoot);
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(root, "original-data"), null, null);
        var directory = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId);
        var fake = new SoftwareFrameApi(readers[0]); var decoder = new OriginalDecoder();
        var factory = new SoftwareFlowFactory(launch, config, fake, false, decoder);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        ParcelOperation[] saved;
        var raw = new byte[] { 30, 70, 140, 210 };
        var rawByFrame = Enumerable.Range(1, 3).ToDictionary(n => (uint)n, n => raw.Select(b => (byte)(b + n - 1)).ToArray());
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } })
        {
            app.Boundary = point => { if (point == "BeforeOriginalFrameWrite") { entered.Set(); release.Wait(); } };
            try
            {
                await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
                await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
                for (uint n = 1; n <= 3; n++) { decoder.Results.Enqueue(["B", "A", "A"]); fake.Enqueue(new(rawByFrame[n], 2, 2), n); }
                Check(entered.Wait(5000), "background writer enters with owned raw pixels");
                var pending = await Wait(app, s => s.Operations.Length == 3 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
                Check(fake.Released == 3 && factory.Source!.Frames == 3 && pending.Operations.All(o => o.OriginalFrame?.State == "Pending"),
                    "blocked image IO does not block SDK acquisition or the three original RCS requests");
                Check(pending.Operations.All(o => o.ParcelCode == "B|A|A" && o.Wcs!.Attempts == 1 &&
                    o.Wcs.Request.IntegrationRequest!.Body.Contains("\"loadCode\":\"B|A|A\"", StringComparison.Ordinal)),
                    "one joined string reaches actual HTTP RCS in each frozen original, without replay");
                var originals = pending.Operations.ToDictionary(o => o.Id, o => Wire.Encode(o.Wcs));
                var stopping = app.StopAsync(default);
                await Task.Delay(60);
                Check(!stopping.IsCompleted, "orderly shutdown waits for actual background original-image IO");
                using (var cancelled = new CancellationTokenSource(20))
                {
                    try { await app.StopAsync(cancelled.Token); throw new Exception("Blocked archive stop returned success"); }
                    catch (OperationCanceledException) { Check(factory.Source!.RetainedOriginalFrames > 0 && !stopping.IsCompleted,
                        "cancelled stop wait retains the actual pending raw-frame writers and their original memory"); }
                }
                release.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(10));
                saved = (await app.SnapshotAsync()).Operations;
                Check(saved.All(o => o.OriginalFrame?.State == "Saved" && Wire.Encode(o.Wcs) == originals[o.Id]) && factory.Source!.RetainedOriginalFrames == 0,
                    "archive completion releases all raw leases without changing original requests, attempts or replies");
                foreach (var operation in saved)
                {
                    var image = operation.OriginalFrame!; var file = Path.Combine(directory, image.RelativeFile);
                    var expectedPixels = rawByFrame[uint.Parse(image.Acquisition.SourceEventId.Split('.').Last())];
                    Check(image.Acquisition == operation.Acquisition && image.RawSha256 == Convert.ToHexString(SHA256.HashData(expectedPixels)) &&
                        image.FileSha256 == Wire.FileHash(file) && DecodeOriginalPng(File.ReadAllBytes(file)).SequenceEqual(expectedPixels),
                        "lossless PNG contains the exact pre-contrast frame tied to this operation, not a later/normalized frame");
                }
            }
            finally { release.Set(); }
        }
        // Simulate only the auxiliary commit being interrupted. All business
        // request/receipt facts and the actual original file remain untouched.
        using (var repository = new ParcelRepository(directory, "sportmaster"))
        {
            var backup = Path.Combine(root, "original-frames-backup.db");
            ParcelRecovery.CreateBackup(directory, backup, config, null, repository.StorageBytes,
                destination => repository.Backup(destination, default), (_, _) => { });
            var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(backup + ".recovery", "COMPLETED.json")));
            Check(saved.All(o => manifest.Files.GetValueOrDefault(o.OriginalFrame!.RelativeFile) == o.OriginalFrame.FileSha256),
                "complete backup automatically includes every saved original-frame file with its exact custody hash");
            File.WriteAllText(Path.Combine(root, "native-original-png.txt"), Path.Combine(backup + ".recovery", saved.Single(o => o.Acquisition!.SourceEventId == "mvs.frame.1").OriginalFrame!.RelativeFile));
            repository.Commit(saved[0] with { OriginalFrame = saved[0].OriginalFrame! with { State = "Pending" } }, "SyntheticArchiveInterruption", "Auxiliary recovery fixture");
        }
        File.WriteAllBytes(Path.Combine(directory, saved[2].OriginalFrame!.RelativeFile), [1, 2, 3]);
        var before = peer.Requests.Count;
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new SoftwareFlowFactory(launch, config, new SoftwareFrameApi(readers[0]), false, new OriginalDecoder())) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
            var restored = (await app.SnapshotAsync()).Operations.Single(o => o.Id == saved[0].Id);
            Check(restored.OriginalFrame?.State == "Interrupted" && Wire.Encode(restored.Wcs) == Wire.Encode(saved[0].Wcs) && peer.Requests.Count == before,
                "restart marks unfinished archive Interrupted without photographing again or replaying the frozen request");
            var corrupted = (await app.SnapshotAsync()).Operations.Single(o => o.Id == saved[2].Id);
            Check(corrupted.OriginalFrame?.State == "Failed" && corrupted.OriginalFrame.FileSha256 == saved[2].OriginalFrame!.FileSha256 &&
                Wire.Encode(corrupted.Wcs) == Wire.Encode(saved[2].Wcs), "changed original image is reported explicitly without replacing evidence hashes or altering the accepted request");
            await app.StopAsync(default);
        }
        var cleanup = Path.Combine(root, "retired-original-cleanup");
        using (var repository = new ParcelRepository(cleanup, "sportmaster"))
        {
            var images = Path.Combine(cleanup, "images/original-frames"); Directory.CreateDirectory(images);
            var retired = Wire.Id(); var retained = Wire.Id();
            File.WriteAllBytes(Path.Combine(images, retired + ".png"), [1]); File.WriteAllBytes(Path.Combine(images, retired + ".png.pending"), [2]);
            File.WriteAllBytes(Path.Combine(images, retained + ".png"), [3]);
            using (var db = new SqliteConnection("Data Source=" + Path.Combine(cleanup, "sportmaster.db")))
            {
                db.Open(); using var c = db.CreateCommand(); c.CommandText = "INSERT INTO parcel_retired_operations VALUES($id,$hash,$at,$run)";
                c.Parameters.AddWithValue("$id", retired); c.Parameters.AddWithValue("$hash", new string('A', 64)); c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); c.Parameters.AddWithValue("$run", Wire.Id()); c.ExecuteNonQuery();
            }
            repository.CleanupRetiredOriginalFrames(() => { });
            Check(!File.Exists(Path.Combine(images, retired + ".png")) && !File.Exists(Path.Combine(images, retired + ".png.pending")) && File.Exists(Path.Combine(images, retained + ".png")),
                "stopped retention cleanup deletes only committed retired identities; live/unknown frame files remain untouched");
        }
        foreach (var archiveCase in new[] { "rejected", "failed", "saved" })
        {
            var reject = archiveCase == "rejected";
            peer.Mode = "assigned";
            var c = config with { BarcodeFilter = reject ? null : config.BarcodeFilter,
                Wcs = config.Wcs! with { Parameters = new(config.Wcs.Parameters) { ["request-timeout-ms"] = archiveCase == "saved" ? "1500" : "150" } } };
            var folder = Path.Combine(root, archiveCase + "-archive-unknown"); var cfg = Path.Combine(folder, "config"); Directory.CreateDirectory(cfg);
            ExtensionStore.AtomicWrite(Path.Combine(cfg, "business.json"), c);
            var run = new BusinessLaunch(cfg, Path.Combine(folder, "data"), null, null);
            var api = new SoftwareFrameApi(readers[0]); var dec = new OriginalDecoder(); var f = new SoftwareFlowFactory(run, c, api, false, dec);
            ParcelOperation? unknown = null;
            await using (var app = new ParcelRuntime(run, new SportmasterFlow(), f) { RuntimeIdentityPreflight = () => { } })
            {
                if (archiveCase == "failed") app.Boundary = point => { if (point == "BeforeOriginalFrameWrite") throw new IOException("Synthetic archive IO failure"); };
                await app.StartAsync(default); await ManagementModeFixture.Authorize(app); await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
                var sent = peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal));
                if (!reject) peer.Mode = "timeout";
                dec.Results.Enqueue(["A", "B"]); api.Enqueue(new(raw, 2, 2), 1);
                if (reject)
                {
                    var s = await Wait(app, s => s.Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Rejected));
                    Check(s.Operations.Length == 0 && f.Source!.RetainedOriginalFrames == 0 &&
                        peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal)) == sent &&
                        !Directory.Exists(Path.Combine(run.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, "images/original-frames")),
                        "strictly rejected multi-code frame releases original pixels and produces no task, image or HTTP request");
                }
                else
                {
                    ParcelOperation? archivedDuringRequest = null;
                    if (archiveCase == "saved")
                    {
                        archivedDuringRequest = (await Wait(app, s => s.Operations.SingleOrDefault() is { Wcs.State: StageState.Dispatching, OriginalFrame.State: "Saved" })).Operations.Single();
                        Check(archivedDuringRequest.Wcs!.Attempts == 1 && archivedDuringRequest.OriginalFrame!.Acquisition == archivedDuringRequest.Acquisition,
                            "original archive can finish while the actual frozen HTTP request remains in flight");
                    }
                    var expectedArchive = archiveCase == "saved" ? "Saved" : "Failed";
                    unknown = (await Wait(app, s => s.Operations.SingleOrDefault() is { State: ParcelState.WcsUnknown } op && op.OriginalFrame?.State == expectedArchive)).Operations.Single();
                    Check(unknown.Wcs is { Attempts: 1, State: StageState.Unknown } && unknown.Wcs.Request.IntegrationRequest!.Body.Contains("A|B") && f.Source!.RetainedOriginalFrames == 0,
                        "background image " + expectedArchive + " cannot clear Unknown, replace original body or add an attempt");
                    if (archivedDuringRequest is not null)
                        Check(Wire.Encode(unknown.OriginalFrame) == Wire.Encode(archivedDuringRequest.OriginalFrame) &&
                            Wire.Encode(unknown.Wcs!.Request) == Wire.Encode(archivedDuringRequest.Wcs!.Request),
                            "later RCS timeout retains the earlier successful archive and exact frozen request instead of restoring Pending");
                }
                await app.StopAsync(default);
            }
            if (unknown is not null)
            {
                peer.Mode = "assigned"; var count = peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal));
                await using var app = new ParcelRuntime(run, new SportmasterFlow(), new SoftwareFlowFactory(run, c, new SoftwareFrameApi(readers[0]), false, new OriginalDecoder())) { RuntimeIdentityPreflight = () => { } };
                await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
                var restored = (await app.SnapshotAsync()).Operations.Single();
                Check(restored.State == ParcelState.WcsUnknown && Wire.Encode(restored.Wcs) == Wire.Encode(unknown.Wcs) && Wire.Encode(restored.OriginalFrame) == Wire.Encode(unknown.OriginalFrame) &&
                    peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal)) == count,
                    "restart retains " + archiveCase + " image and original Unknown independently without automatic replay");
                await app.StopAsync(default);
            }
        }
        Console.WriteLine("SM_ORIGINAL_FRAME_PASS; synthetic SDK/decoder; actual PNG/SQLite/HTTP; hardware NOT_RUN; root=" + root);
    }
    static byte[] DecodeOriginalPng(byte[] png)
    {
        using var compressed = new MemoryStream(); int width = 0, height = 0;
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4)); var kind = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (kind == "IHDR") { width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset + 8)); height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset + 12)); }
            if (kind == "IDAT") compressed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0; using var zlib = new ZLibStream(compressed, CompressionMode.Decompress); using var pixels = new MemoryStream();
        var row = new byte[width]; for (var i = 0; i < height; i++) { if (zlib.ReadByte() != 0) throw new InvalidDataException("Unexpected PNG filter"); zlib.ReadExactly(row); pixels.Write(row); }
        return pixels.ToArray();
    }
}
