using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ReliabilityHardening
{
    static async Task ResourceCycles(string workspace, string root)
    {
        var c = Config(workspace); var launch = Launch(root, c); List<object> trend = [];
        for (var i = 0; i < 10; i++)
        {
            await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch));
            await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
            Check(app.EmitReferenceAt(i % 2 == 0 ? "left" : "right", "cycle-" + i, "001", "cycle-event-" + i), "cycle " + i + " explicit new entity accepted");
            await Until(() => app.WorkerProgress().Sum(x => x.Processed) == 1);
            var now = DateTimeOffset.UtcNow;
            var query = new SoftwareLogQuery(now.AddHours(-1), now.AddHours(1));
            var page = await app.QueryLogsAsync(query, null, 0, default);
            _ = await app.QueryLogsAsync(query, page.Selection, 0, default);
            await app.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
            using var progress = JsonDocument.Parse(Wire.Encode(app.RuntimeProgress()));
            Check(progress.RootElement.GetProperty("ReadOperations").GetInt32() == 0 && progress.RootElement.GetProperty("AdministrationOperations").GetInt32() == 0 &&
                progress.RootElement.GetProperty("ManagementStatusOperations").GetInt32() == 0 && app.WorkerProgress().All(p => p.State == "Stopped"),
                "cycle " + i + " actual stop drains reads, authority observers and both consumers");
            await app.DisposeAsync();
            Check(!app.HasUnreleasedResources && !app.HasActiveWork, "cycle " + i + " disposal releases ownership before the next startup");
            using var process = Process.GetCurrentProcess();
            trend.Add(new { Cycle = i, process.HandleCount, Threads = process.Threads.Count, process.PrivateMemorySize64, process.WorkingSet64,
                ReadOperations = 0, OwnedRuntimeResources = false, DbBytes = new FileInfo(Path.Combine(Data(launch, c), "sportmaster.db")).Length });
        }
        File.WriteAllText(Path.Combine(root, "resource-trend.json"), Wire.Encode(trend));
        Console.WriteLine("MEASURE H02 ten startup/query/stop/disposal cycles=" + Wire.Encode(trend));
        Check(Scalar(Path.Combine(Data(launch, c), "sportmaster.db"), "SELECT count(*) FROM parcel_ingress WHERE state='Processed'") == 10,
            "ten repeated ownership cycles preserve all accepted inputs without replay or duplicate entity execution");
    }
}
