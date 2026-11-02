using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    static async Task TimeoutRescanChecks(string workspace, string root)
    {
        await using var peer = new Peer { Mode = "timeout-left" };
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.0.0",
            Parameters = new(Binding(peer.Endpoint).Parameters) { ["request-timeout-ms"] = "300" } };
        var c = HeartbeatConfiguration(workspace, binding) with { TimeoutMs = 1500 };
        var readers = c.Topology!.Readers.Select((r, i) => i != 0 ? r : r with { Driver = "tcp-line", Parameters = new()
        { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["entity.mode"] = "line" } }).ToArray();
        c = c with { Reader = readers[0], Topology = c.Topology with { Readers = readers } };
        var directory = Path.Combine(root, "timeout-rescan"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), c);
        var launch = new BusinessLaunch(directory, Path.Combine(directory, "data"), null, null);
        var database = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId, "sportmaster.db");
        ParcelRuntime Open() => new(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        SqliteConnection ReadDatabase()
        { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open(); return db; }
        ParcelOperation Persisted(string id)
        {
            using var db = ReadDatabase(); using var query = db.CreateCommand(); query.CommandText = "SELECT body FROM parcel_operations WHERE id=$id"; query.Parameters.AddWithValue("$id", id);
            return Wire.Decode<ParcelOperation>((string)query.ExecuteScalar()!);
        }
        async Task Scan(TcpClient camera) => await camera.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
        string originalId, originalStage, originalBody; long originalWireId, secondWireId;
        await using (var app = Open())
        {
            await app.StartAsync(default);
            using var camera = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
            await Scan(camera);
            await Wait(app, s => s.Operations.Any(o => o.PositionId == "left" && o.Wcs is not null));
            Check(app.EmitReferenceAt("right", "right-during-left-timeout", "SYNTH-0001", Wire.Id()), "other position accepts an independent scan while the first request is waiting");
            var timed = await Wait(app, s => s.Operations.Length == 2 && s.Operations.Any(o => o.State == ParcelState.WcsUnknown) && s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            var original = timed.Operations.Single(o => o.PositionId == "left"); originalId = original.Id;
            var stored = Persisted(originalId); originalStage = Wire.Encode(stored.Wcs); originalBody = stored.Wcs!.Request.IntegrationRequest!.Body; originalWireId = stored.Wcs.Request.IntegrationRequest.RequestId;
            Check(!timed.Positions!.Single(p => p.Id == "left").Admit && timed.Positions!.Single(p => p.Id == "right").Admit && !new SportmasterFlow().MainSettled(original) && stored.Wcs is { Attempts: 1, State: StageState.Unknown },
                "OP29: a local timeout closes the affected position without declaring the old request settled or blocking the other position");
            var view = new SupplyStationViewModel(app); await view.RefreshAsync();
            Check(!view.Left.CanAdmit && view.Left.AttentionReason.Contains("暂勿投递", StringComparison.Ordinal) && view.Left.AttentionReason.Contains("暂停后续读码", StringComparison.Ordinal) &&
                !view.Left.AttentionReason.Contains("重新扫描", StringComparison.Ordinal) && !view.Left.SlotAvailable,
                "OP29: the unresolved position has no chute and explicitly stops subsequent scanning");
            Check(!new SportmasterFlow().AllowsNextAdmissionAfter(original with { Wcs = original.Wcs! with { State = StageState.Conflict } }) &&
                !new Ipc.Application.DirectPlc.DirectPlcFlow().AllowsNextAdmissionAfter(original), "receipt conflicts and other applications do not bypass the original-result guard");
            peer.Mode = "assigned"; await Scan(camera);
            var blocked = await Wait(app, s => s.Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Blocked));
            Check(blocked.Operations.Length == 2 && peer.Requests.Count == 2, "OP29: a subsequent input cannot send a new left request while the original is unknown");
            Check(app.EmitReferenceAt("right", "right-after-left-timeout", original.ParcelCode, Wire.Id()), "other position accepts another independent same-barcode scan");
            var rescanned = await Wait(app, s => s.Operations.Length == 3 && s.Operations.Count(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved) == 2);
            var next = rescanned.Operations.Where(o => o.PositionId == "right").OrderByDescending(o => o.ObservedAt).First(); var nextStored = Persisted(next.Id); secondWireId = nextStored.Wcs!.Request.IntegrationRequest!.RequestId;
            Check(next.ParcelCode == original.ParcelCode && next.AdmissionId != original.AdmissionId && next.Wcs!.Request.Id != original.Wcs!.Request.Id && secondWireId > originalWireId &&
                peer.Requests.Count == 3 && peer.Requests.Count(r => r.Body == originalBody) == 1 && Wire.Encode(Persisted(originalId).Wcs) == originalStage,
                "same barcode on the other position creates a fresh identity/request ID and leaves the unknown original untouched");
            using (var db = ReadDatabase())
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT count(*) FROM parcel_audit WHERE event='InputObserved' AND json_extract(detail,'$.PositionId')='left'";
                Check((long)query.ExecuteScalar()! >= 2, "blocked left input retains its audit evidence without preparing another external intention");
            }
            await Task.Delay(2700); await view.RefreshAsync();
            Check(!view.Left.SlotAvailable && view.Right.OperationId == next.Id && view.Right.SlotAvailable && view.Right.Slot == "SYNTH-CHUTE" && Wire.Encode(Persisted(originalId).Wcs) == originalStage && peer.Requests.Count == 3,
                "late original response neither restores a left chute nor overwrites the other position's result");
            var history = await app.HistoryAsync();
            Check(history.Items.Any(h => h.Operation.Id == originalId && h.Operation.State == ParcelState.WcsUnknown) && history.Items.Any(h => h.Operation.Id == next.Id && h.Operation.State == ParcelState.SlotResolved),
                "history retains the left timeout and right success as separate requests");
        }
        await using (var app = Open())
        {
            await app.StartAsync(default);
            using var camera = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Wait(app, s => s.Devices!.Any(d => d.DeviceId == readers[0].DeviceId && d.IsValid(DateTimeOffset.UtcNow)));
            var recovered = await app.SnapshotAsync();
            Check(recovered.Paused && peer.Requests.Count == 3 && Wire.Encode(Persisted(originalId).Wcs) == originalStage,
                "restart retains all requests and normal startup pause without replaying the timeout");
            await ManagementModeFixture.Authorize(app); var reconciliation = await app.ReconcileAsync(originalId);
            Check(reconciliation.Status == "QueryUnsupported" && Wire.Encode(Persisted(originalId).Wcs) == originalStage && peer.Requests.Count == 3,
                "unsupported query is refused without changing the original timeout/receipts or invoking an external endpoint");
            for (var i = 0; i < 20; i++)
            {
                var snapshot = await app.SnapshotAsync(); var receipt = await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
                if (receipt.Status != "Conflict") { Check(receipt.Status == "Persisted", "explicit resume can restore the healthy position without clearing the retained timeout"); break; }
            }
            await Wait(app, s => !s.Paused && !s.Positions!.Single(p => p.Id == "left").Admit && s.Positions!.Single(p => p.Id == "right").Admit);
            await Scan(camera);
            await Wait(app, s => s.Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Blocked));
            Check(peer.Requests.Count == 3, "OP29: restart/resume cannot authorize the blocked position to scan around the original unknown request");
            Check(app.EmitReferenceAt("right", "right-after-restart", "SYNTH-0001", Wire.Id()), "the healthy position can continue after restart");
            var final = await Wait(app, s => s.Operations.Length == 4 && s.Operations.Count(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved) == 3);
            var last = final.Operations.Where(o => o.PositionId == "right").OrderByDescending(o => o.ObservedAt).First();
            Check(Persisted(last.Id).Wcs!.Request.IntegrationRequest!.RequestId > secondWireId && peer.Requests.Count == 4 && Wire.Encode(Persisted(originalId).Wcs) == originalStage,
                "new healthy-position scans advance the durable request ID while the original timeout remains unchanged");
        }
    }
}
