using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    public static async Task P0UnknownRestart(string workspace, string root)
    {
        await using var peer = new Peer { Mode = "timeout" };
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.0.0" }; binding.Parameters.Remove("quantity");
        binding.Parameters["request-timeout-ms"] = "1000"; // Current explicit deadline must expire before the 2.5 s reply.
        var c = HeartbeatConfiguration(workspace, binding);
        Directory.CreateDirectory(root); ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), c);
        var launch = new BusinessLaunch(root, Path.Combine(root, "data"), null, null);
        ParcelRuntime Open() => new(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        string Frozen()
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId, "sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
            using var query = db.CreateCommand(); query.CommandText = "SELECT body FROM parcel_operations";
            return Wire.Encode(Wire.Decode<ParcelOperation>((string)query.ExecuteScalar()!).Wcs!.Request);
        }
        string original;
        await using (var app = Open())
        {
            await app.StartAsync(default); await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
            app.EmitReferenceAt("left", "synthetic-p0-unknown", "000123", Wire.Id());
            await Wait(app, s => s.Operations.SingleOrDefault()?.State == ParcelState.WcsUnknown); original = Frozen();
        }
        Check(peer.Requests.Count == 1, "lost HTTP response produces exactly one original request");
        await Task.Delay(2700); // Let the existing timeout stub finish before its listener is disposed.
        peer.Mode = "assigned";
        await using (var app = Open())
        {
            await app.StartAsync(default); var recovered = (await app.SnapshotAsync()).Operations.Single();
            Check(recovered.State == ParcelState.WcsUnknown && Frozen() == original, "process restart preserves Unknown and frozen original bytes");
            await ManagementModeFixture.Authorize(app);
            await app.ReconcileAsync(recovered.Id); await app.ReconnectAsync(); await Task.Delay(150);
            var current = (await app.SnapshotAsync()).Operations.Single();
            Check(current.State == ParcelState.WcsUnknown && current.Wcs!.Attempts == 1 && peer.Requests.Count == 1 && Frozen() == original,
                "restart, unavailable authoritative query and reconnect neither clear Unknown nor replay");
        }
    }
}
