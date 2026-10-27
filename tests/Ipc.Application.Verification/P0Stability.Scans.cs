using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    public static async Task P0ScanRegression(string workspace, string root)
    {
        await using var peer = new Peer();
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.3.5" };
        binding.Parameters.Remove("quantity");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var c = HeartbeatConfiguration(workspace, binding);
        var readers = c.Topology!.Readers.Select((r, i) => i != 0 ? r : r with { Driver = "tcp-line", Parameters = new()
        { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["entity.mode"] = "line", ["bcr-code"] = "TEST-LEFT-READER" } }).ToArray();
        c = c with { Reader = readers[0], Topology = c.Topology with { Readers = readers } };
        Directory.CreateDirectory(root); ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), c);
        var launch = new BusinessLaunch(root, Path.Combine(root, "data"), null, null);
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            using var camera = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
            foreach (var code in new[] { "000123", "000123", "000456" })
            {
                var count = peer.Requests.Count;
                await camera.GetStream().WriteAsync(Encoding.UTF8.GetBytes(code[..3])); await Task.Delay(30);
                Check(peer.Requests.Count == count, "partial frame never sends RCS");
                await camera.GetStream().WriteAsync(Encoding.UTF8.GetBytes(code[3..] + "\n"));
                await Wait(app, s => s.Operations.Count(o => o.State == ParcelState.SlotResolved) == count + 1);
            }
            var snapshot = await app.SnapshotAsync();
            Check(snapshot.Operations.Length == 3 && snapshot.Operations.Select(x => x.AdmissionId).Distinct().Count() == 3 && peer.Requests.Count == 3,
                "three scans including repeated barcode own three entities and exactly three requests");
            Check(peer.Requests.Select(x => JsonDocument.Parse(x.Body).RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString()).SequenceEqual(new[] { "000123", "000123", "000456" }), "original barcode bytes/order preserved");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId, "sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
            using var query = db.CreateCommand(); query.CommandText = "SELECT body FROM parcel_operations";
            using var rows = query.ExecuteReader(); var persisted = new List<ParcelOperation>();
            while (rows.Read()) persisted.Add(Wire.Decode<ParcelOperation>(rows.GetString(0)));
            Check(persisted.Count == 3 && persisted.All(o => o.Wcs is { Attempts: 1 } && peer.Requests.Count(p => p.Body == o.Wcs.Request.IntegrationRequest!.Body) == 1), "durable frozen body equals the actual sole HTTP request; normal UI remains redacted");
        }
        var legacy = binding with { Parameters = new(binding.Parameters) { ["quantity"] = "7" } };
        var previousRequests = peer.Requests.Count; var refused = false;
        try { await using var unsupported = new SportmasterRcsTaskService(legacy); }
        catch (InvalidDataException) { refused = true; }
        Check(refused && peer.Requests.Count == previousRequests, "obsolete quantity parameter refuses before any business send");
        await using var service = new SportmasterRcsTaskService(binding);
        service.ConfigureRuntime(HeartbeatConfiguration(workspace, binding)); await service.StartAsync(default);
        var request = Request(binding); var prepared = service.PrepareRequest(request, 99);
        var body = prepared.Body.Replace("\"quantity\":1", "\"quantity\":7", StringComparison.Ordinal);
        var frozen = request with { IntegrationRequest = prepared with { Body = body, BodyHash = Wire.Hash(body) } };
        var original = Wire.Encode(frozen);
        refused = false;
        try { await service.RequestDestinationAsync(frozen, default); }
        catch (InvalidDataException) { refused = true; }
        Check(refused && Wire.Encode(frozen) == original && peer.Requests.Count == previousRequests,
            "unsupported frozen quantity preserves original bytes and refuses execution without resending or rewriting");
    }
}
