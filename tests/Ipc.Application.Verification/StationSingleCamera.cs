using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class StationRecoveryChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL RECOVERY01 " + message); Console.WriteLine("PASS RECOVERY01 " + message); }
    static void Reject<T>(Action action, string message) where T : Exception
    { try { action(); throw new Exception("Expected rejection: " + message); } catch (T) { Check(true, message); } }
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "password-recovery"); var now = DateTimeOffset.UtcNow;
        var mode = new LocalManagementMode(folder) { Clock = () => now };
        Reject<InvalidOperationException>(() => mode.ResetPassword("Cainiao123", "22222"), "root recovery cannot replace first setup");
        mode.ConfigurePassword("11111"); Check(mode.Enter("11111"), "ordinary password grants a real session");
        var cancellation = mode.Require(); var grant = mode.Delegate(Wire.Hash("original-maintenance"));
        var file = Path.Combine(folder, "security/management-mode.json");
        string Credential() { using var document = JsonDocument.Parse(File.ReadAllText(file)); return document.RootElement.GetProperty("Credential").GetRawText(); }
        var before = Credential();
        Reject<UnauthorizedAccessException>(() => mode.ResetPassword("wrong-root", "22222"), "wrong root rejects recovery");
        Check(Credential() == before && mode.Status().Authorized && !cancellation.IsCancellationRequested, "failed recovery preserves credential and current grant");
        Reject<System.IO.InvalidDataException>(() => mode.ResetPassword("Cainiao123", "1234"), "replacement uses the real password policy");
        Check(Credential() == before, "invalid replacement does not overwrite the original credential");
        mode.ResetPassword("Cainiao123", "22222");
        Check(cancellation.IsCancellationRequested && !mode.Status().Authorized, "successful recovery revokes the existing session");
        Reject<UnauthorizedAccessException>(() => mode.ValidateDelegation(grant, Wire.Hash("original-maintenance"), false), "old delegated maintenance authority is revoked");
        Check(!mode.Enter("11111") && mode.Enter("22222"), "only the replacement password authenticates");
        mode.Exit(); Check(!mode.Enter("Cainiao123"), "root password is not a management login password");
        var bytes = File.ReadAllText(file);
        Check(!bytes.Contains("Cainiao123") && !bytes.Contains("22222") && bytes.Contains("ManagementPasswordRecovered"), "recovery retains a non-secret audit and salted credential");
        for (var i = 0; i < 5; i++) Reject<UnauthorizedAccessException>(() => mode.ResetPassword("wrong-root", "33333"), "wrong recovery attempt " + i);
        var restarted = new LocalManagementMode(folder) { Clock = () => now };
        Reject<UnauthorizedAccessException>(() => restarted.ResetPassword("Cainiao123", "33333"), "recovery lock persists across service instances");
        Check(restarted.Enter("22222"), "recovery lock does not disable the ordinary valid password");
        now = now.AddMinutes(6); restarted.ResetPassword("Cainiao123", "33333");
        Check(!restarted.Status().Authorized && restarted.Enter("33333"), "recovery works after the bounded lock expires");
        var retained = File.ReadAllBytes(file); File.Move(file, file + ".retained");
        Reject<System.IO.InvalidDataException>(() => restarted.ResetPassword("Cainiao123", "44444"), "recovery refuses a missing configured credential ledger");
        Check(!File.Exists(file) && retained.SequenceEqual(File.ReadAllBytes(file + ".retained")), "missing-ledger failure preserves original bytes");
    }
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
                { ["request-timeout-ms"] = "300", ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION" } };
            var readers = ParcelBindings.Readers(seed).ToArray();
            var c = seed with { StationCode = "SYNTH-STATION", Wcs = binding,
                DeferredConnections = Enumerable.Range(0, 3).Where(i => (mask & (1 << i)) != 0).Select(i => i == 2 ? binding.InstanceId : readers[i].DeviceId).ToArray() };
            var folder = Path.Combine(root, "single-" + mask); Directory.CreateDirectory(folder); ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), c);
            var launch = new BusinessLaunch(folder, Path.Combine(folder, "data"), null, null);
            await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            await app.StartAsync(default);
            async Task<ParcelSnapshot> Wait(Func<ParcelSnapshot, bool> ready)
            { for (var i = 0; i < 200; i++) { var s = await app.SnapshotAsync(); if (ready(s)) return s; await Task.Delay(25); } throw new TimeoutException("Single-camera state did not converge: " + Wire.Encode(await app.SnapshotAsync())); }
            var allowed = (mask & 4) == 0 && (mask & 3) != 3;
            var snapshot = allowed ? await Wait(s => s.Availability!.Resume) : await app.SnapshotAsync();
            Check(snapshot.Operations.Length == 0 && snapshot.Availability!.Resume == allowed, "mask " + mask + " resumes only with RCS and at least one ready camera");
            foreach (var position in ParcelBindings.Positions(c).Where(p => ParcelConnectionSetup.IsDeferred(c, p.ReaderDeviceId)))
                Check(!snapshot.Positions!.Single(p => p.Id == position.Id).Admit && !app.EmitReferenceAt(position.Id, "deferred-entity", "000123456", Wire.Id()), "mask " + mask + " never starts or admits the unconfigured camera");
            if (!allowed) continue;
            await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
            var selected = (mask & 1) == 0 ? "left" : "right";
            Check(app.EmitReferenceAt(selected, "single-entity", "000123456", Wire.Id()), "configured " + selected + " accepts its own synthetic observation");
            snapshot = await Wait(s => s.Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            var original = snapshot.Operations.Single();
            Check(original.PositionId == selected && original.Wcs!.Attempts == 1 && original.Wcs.State == StageState.Accepted, "single camera completes one actual loopback RCS request");
            var request = Wire.Encode(original.Wcs!.Request); var sent = peer.Requests.Count;
            await ManagementModeFixture.Authorize(app);
            await app.ResetManagementPasswordAsync("Cainiao123", "Recovered-12345");
            Check(!(await app.ManagementModeStatusAsync()).Authorized, "Application recovery requires a new management login");
            snapshot = await app.SnapshotAsync();
            Check(Wire.Encode(snapshot.Operations.Single().Wcs!.Request) == request && peer.Requests.Count == sent, "recovery preserves original request and sends no duplicate");
            if (mask == 0)
            {
                peer.Mode = "timeout"; Check(app.EmitReferenceAt("right", "unknown-single-entity", "000987654", Wire.Id()), "independent position admits the Unknown recovery fixture");
                snapshot = await Wait(s => s.Operations.Any(o => o.State == ParcelState.WcsUnknown));
                var unknown = snapshot.Operations.Single(o => o.State == ParcelState.WcsUnknown);
                var frozen = Wire.Encode(unknown.Wcs!.Request);
                Check(await app.EnterManagementModeAsync("Recovered-12345"), "replacement password enters for Unknown recovery verification");
                await app.ResetManagementPasswordAsync("Cainiao123", "Recovered-67890");
                snapshot = await app.SnapshotAsync(); var retained = snapshot.Operations.Single(o => o.Id == unknown.Id);
                Check(retained.State == ParcelState.WcsUnknown && retained.Wcs!.Attempts == 1 && Wire.Encode(retained.Wcs.Request) == frozen,
                    "root recovery preserves Unknown identity, frozen request and its sole send attempt");
            }
        }
        StationRecoveryChecks.Run(root);
    }
}
