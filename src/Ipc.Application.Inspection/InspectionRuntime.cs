using Ipc.Platform;
namespace Ipc.Inspection;

public sealed record InspectionConfiguration(int SchemaVersion, string StationId, RunMode Mode, int MaximumSamples);
public sealed record InspectionSample(string Id, string Value, DateTimeOffset At);
public sealed record InspectionBatch(string Id, string State, InspectionSample[] Samples);
public sealed record InspectionCommandRecord(string Hash, BusinessReceipt Receipt);
public sealed record InspectionLedger(long Revision, InspectionBatch[] Batches, Dictionary<string, InspectionCommandRecord> Commands);

// Functional non-Supply verification business: durable bounded inspection batches,
// command identity/CAS, admission, recovery, view sharing and exclusive ownership.
public sealed class InspectionRuntime(BusinessLaunch launch) : IBusinessRuntime, IBusinessApplication
{
    public string Id => "inspection";
    public string[] Dependencies => [];
    public BusinessPresentation Presentation { get; } = new(["inspection", "inspection-history"], ["standard", "compact"]);
    public IBusinessApplication Application => this;
    readonly Guid runtimeId = Guid.NewGuid();
    readonly SemaphoreSlim gate = new(1, 1);
    ResourceLease? lease;
    InspectionConfiguration? configuration;
    InspectionLedger ledger = new(0, [], []);
    string? path;
    bool stopped, disposed;
    public Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (path is not null) throw new InvalidOperationException("Inspection startup is single-use");
        MachineRuntimePolicy.RequireRuntimeIdentity();
        configuration = Wire.Decode<InspectionConfiguration>(File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "business.json")));
        if (configuration.SchemaVersion != 1 || !ExtensionSchema.IsId(configuration.StationId) || configuration.Mode != RunMode.Simulation || configuration.MaximumSamples is < 1 or > 1000)
            throw new InvalidDataException("Inspection reference configuration is invalid; Production has no approved implementation");
        var directory = Path.Combine(launch.DataRoot, configuration.Mode.ToString(), configuration.StationId);
        lease = new(directory, ["inspection-ledger"]);
        path = Path.Combine(directory, "inspection.json");
        if (File.Exists(path)) ledger = Wire.Decode<InspectionLedger>(File.ReadAllText(path));
        else ExtensionStore.AtomicWrite(path, ledger);
        return Task.CompletedTask;
    }
    public async Task<BusinessReceipt> CommandAsync(BusinessCommand command)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (path is null || stopped || disposed) return new("NotReady", "Inspection is not running", ledger.Revision);
            if (!Guid.TryParseExact(command.Id, "N", out _)) return new("Rejected", "Stable command ID required", ledger.Revision);
            if (ledger.Commands.TryGetValue(command.Id, out var prior)) return prior.Hash == Wire.Hash(Wire.Encode(command)) ? prior.Receipt : new("Rejected", "Command identity was reused with different content", ledger.Revision);
            if (command.ExpectedRevision != ledger.Revision) return new("Conflict", "Refresh the current projection", ledger.Revision);
            var batches = ledger.Batches.ToList(); var active = batches.LastOrDefault(x => x.State == "Open");
            switch (command.Kind)
            {
                case "BeginBatch" when active is null && ExtensionSchema.IsId(command.Value):
                    if (batches.Any(x => x.Id == command.Value)) return new("Rejected", "Batch identity is retained", ledger.Revision);
                    batches.Add(new(command.Value!, "Open", [])); break;
                case "RecordSample" when active is not null && active.Samples.Length < configuration!.MaximumSamples && command.Value is { Length: > 0 and <= 256 } && !command.Value.Any(char.IsControl):
                    batches[^1] = active with { Samples = active.Samples.Append(new(command.Id, command.Value, DateTimeOffset.UtcNow)).ToArray() }; break;
                case "CloseBatch" when active is { Samples.Length: > 0 }:
                    batches[^1] = active with { State = "Closed" }; break;
                default: return new("Rejected", "Unknown command or unmet batch/sample guard", ledger.Revision);
            }
            if (batches.Count > 1000 || ledger.Commands.Count >= 10000) return new("Rejected", "Retention budget requires explicit maintenance", ledger.Revision);
            var receipt = new BusinessReceipt("Persisted", "Inspection fact committed", ledger.Revision + 1);
            var next = new InspectionLedger(receipt.Revision, batches.ToArray(), new(ledger.Commands) { [command.Id] = new(Wire.Hash(Wire.Encode(command)), receipt) });
            ExtensionStore.AtomicWrite(path, next); ledger = next; return receipt;
        }
        finally { gate.Release(); }
    }
    public async Task<BusinessProjection> QueryAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { return new(runtimeId, ledger.Revision, stopped ? "Stopped" : path is null ? "NotReady" : "Ready", new() {
            ["Business"] = Id, ["Batches"] = ledger.Batches.Length.ToString(), ["Samples"] = ledger.Batches.Sum(x => x.Samples.Length).ToString(),
            ["CurrentBatch"] = ledger.Batches.LastOrDefault()?.Id ?? "", ["CurrentState"] = ledger.Batches.LastOrDefault()?.State ?? "Empty" }); }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { stopped = true; } finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lease?.Dispose(); disposed = true;
    }
}
