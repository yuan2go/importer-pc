using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class StationRecoveryChecks
{
    public static void Run(string root) => IndustrialRecovery.Run(root);
}

static partial class SportmasterRcs
{
    public static async Task SingleCamera(string workspace, string root)
    {
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        for (var mask = 0; mask < 8; mask++)
        {
            await using var peer = new Peer();
            var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Parameters = new(Binding(peer.Endpoint).Parameters)
                { ["request-timeout-ms"] = RcsRequestPolicy.DefaultTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                  ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION" } };
            var readers = ParcelBindings.Readers(seed).ToArray();
            var c = seed with { StationCode = "SYNTH-STATION", Wcs = binding,
                DeferredConnections = Enumerable.Range(0, 3).Where(i => (mask & (1 << i)) != 0).Select(i => i == 2 ? binding.InstanceId : readers[i].DeviceId).ToArray() };
            var folder = Path.Combine(root, "single-" + mask); Directory.CreateDirectory(folder); ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), c);
            var launch = new BusinessLaunch(folder, Path.Combine(folder, "data"), null, null);
            await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            await app.StartAsync(default);
            async Task<ParcelSnapshot> Wait(Func<ParcelSnapshot, bool> ready)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                while (System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20))
                {
                    var current = await app.SnapshotAsync();
                    if (ready(current)) return current;
                    await Task.Delay(25);
                }
                throw new TimeoutException("Single-camera state did not converge: " + Wire.Encode(await app.SnapshotAsync()));
            }
            var allowed = (mask & 4) == 0; // Current SM also supports explicit manual input with both cameras deferred.
            var snapshot = allowed ? await Wait(s => s.Availability!.Resume) : await app.SnapshotAsync();
            Check(snapshot.Operations.Length == 0 && snapshot.Availability!.Resume == allowed, "mask " + mask + " resumes only with RCS; manual input is independent of camera configuration");
            foreach (var position in ParcelBindings.Positions(c).Where(p => ParcelConnectionSetup.IsDeferred(c, p.ReaderDeviceId)))
                Check(snapshot.Positions!.Single(p => p.Id == position.Id).BarcodeInput?.Camera == false && !app.EmitReferenceAt(position.Id, "deferred-entity", "000123456", Wire.Id()), "mask " + mask + " never starts or admits the unconfigured camera");
            if (!allowed) continue;
            if ((mask & 3) == 3)
            {
                Check(snapshot.Positions!.All(p => p.BarcodeInput?.Manual == true), "both deferred cameras retain the independent manual input contract");
                continue; // This fixture's remaining steps inject camera observations; manual submissions have their own full fixture.
            }
            await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
            var selected = (mask & 1) == 0 ? "left" : "right";
            Check(app.EmitReferenceAt(selected, "single-entity", "000123456", Wire.Id()), "configured " + selected + " accepts its own synthetic observation");
            snapshot = await Wait(s => s.Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            await ManagementModeFixture.Authorize(app);
            var original = (await app.OperationAsync(snapshot.Operations.Single().Id))!;
            Check(original.PositionId == selected && original.Wcs!.Attempts == 1 && original.Wcs.State == StageState.Accepted, "single camera completes one actual loopback RCS request");
            Check(RcsRequestPolicy.TimeoutMs(original.Wcs!.Request.Binding) == RcsRequestPolicy.DefaultTimeoutMs,
                "functional success uses the real default frozen request budget, not a short latency assertion");
            Check(original.Wcs.Request.Payload.Length > 0 && original.Wcs.Request.Binding.Target == peer.Endpoint,
                "recovery comparison reads authenticated original payload and target, not the redacted operator projection");
            var request = Wire.Encode(original.Wcs!.Request); var sent = peer.Requests.Count;
            app.ManagementPasswordResetApproval = replacement =>
            {
                var mode = LocalManagementMode.ForRoot(launch.DataRoot);
                var syntheticCaller = new ManagementRecoveryCaller("SYNTHETIC", false, 100, 12345, 1);
                var challenge = mode.IssueRecoveryChallenge(syntheticCaller);
                mode.CompleteRecoveryCore(challenge, replacement, syntheticCaller with { Administrator = true });
                return Task.CompletedTask;
            };
            await app.ResetManagementPasswordAsync("Recovered-12345");
            Check(!(await app.ManagementModeStatusAsync()).Authorized, "Application recovery requires a new management login");
            Check(await app.EnterManagementModeAsync("Recovered-12345"), "explicit replacement-password login required to inspect original bytes");
            var preserved = (await app.OperationAsync(original.Id))!;
            Check(Wire.Encode(preserved.Wcs!.Request) == request && peer.Requests.Count == sent, "recovery preserves original request and sends no duplicate");
            if (mask == 0)
            {
                peer.Mode = "withhold-response"; Check(app.EmitReferenceAt("right", "unknown-single-entity", "000987654", Wire.Id()), "independent position admits the Unknown recovery fixture");
                snapshot = await Wait(s => s.Operations.Any(o => o.State == ParcelState.WcsUnknown));
                var unknown = snapshot.Operations.Single(o => o.State == ParcelState.WcsUnknown);
                Check(unknown.Wcs!.LocalFailure == RequestLocalFailure.DeadlineExpired && peer.Requests.Count == sent + 1,
                    "withheld response reaches the real frozen deadline with exactly one business send");
                var frozen = Wire.Encode(unknown.Wcs!.Request);
                Check(await app.EnterManagementModeAsync("Recovered-12345"), "replacement password enters for Unknown recovery verification");
                await app.ResetManagementPasswordAsync("Recovered-67890");
                Check(!(await app.ManagementModeStatusAsync()).Authorized && await app.EnterManagementModeAsync("Recovered-67890"),
                    "Unknown recovery also revokes authority before explicit original-byte inspection");
                var retained = (await app.OperationAsync(unknown.Id))!;
                Check(retained.State == ParcelState.WcsUnknown && retained.Wcs!.Attempts == 1 && Wire.Encode(retained.Wcs.Request) == frozen,
                    "Windows-admin recovery core preserves Unknown identity, frozen request and its sole send attempt");
            }
        }
        StationRecoveryChecks.Run(root);
    }
}
