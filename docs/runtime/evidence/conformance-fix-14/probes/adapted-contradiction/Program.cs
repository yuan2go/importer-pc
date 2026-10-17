using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;

// Independent, all-synthetic audit probe. This program opens no network sockets and
// creates no hardware source. It invokes the actual public application/T1/T2/T3 APIs
// and supplies typed synthetic responses. The audited repository is not modified.
var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "ipc-audit-contradiction-" + Wire.Id());
Directory.CreateDirectory(directory);
var database = Path.Combine(directory, "station.db");
if (File.Exists(database)) throw new InvalidOperationException("Probe requires a fresh isolated data directory");
var config = new StationConfig(1, "SIM-AUDIT-CONTRADICTION", "AUDIT-1", RunMode.Simulation,
    [new("left", "manual", "AUDIT-CAM-L", "r1"), new("right", "manual", "AUDIT-CAM-R", "r1")],
    new("audit-fixture", "r1", "SIMULATION-v1", "http://127.0.0.1:65530/"));
string requestId = "";
object acceptedProjection, contradictoryProjection, storedAfterContradiction, restartedProjection;

async Task<CommandReceipt> Command(StationApplication app, string kind, string? owner = null, string? value = null)
{
    var snapshot = await app.SnapshotAsync();
    var receipt = await app.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, kind, owner, value));
    if (receipt.Status != "Persisted") throw new InvalidOperationException("Unexpected " + kind + ": " + Wire.Encode(receipt));
    return receipt;
}
async Task Bootstrap(StationApplication app, string epoch)
{
    await app.SetDeviceAsync("AUDIT-CAM-L", "left-" + epoch, DeviceHealth.Ready);
    await app.SetDeviceAsync("AUDIT-CAM-R", "right-" + epoch, DeviceHealth.Ready);
    await app.SetWcsHealthAsync(true);
    await Command(app, "Login", value: "maintenance");
}
async Task<(BusinessRequest Request, SendAttempt Attempt, IntegrationResponse Response)> AcceptNext(StationApplication app, RequestKind kind)
{
    var claim = await app.ClaimAsync() ?? throw new InvalidOperationException("No claim for " + kind);
    if (claim.Request.Kind != kind) throw new InvalidOperationException("Wrong request kind");
    if (!await app.BeforeSendAsync(claim.Request, claim.Attempt)) throw new InvalidOperationException("Synthetic handoff was denied");
    var payload = Wire.Decode<FixturePayload>(claim.Request.Payload);
    var response = new IntegrationResponse(claim.Request.Id, claim.Request.PayloadHash, "audit-evidence-" + kind,
        "Accepted", "AUDIT synthetic accepted " + kind,
        Remaining: kind == RequestKind.Validate ? 10 : null,
        Quantity: kind == RequestKind.Validate ? 1 : kind == RequestKind.Submit ? payload.Quantity : null);
    await app.CompleteAsync(claim.Request, claim.Attempt, response);
    return (claim.Request, claim.Attempt, response);
}
async Task<object> Projection(StationApplication app)
{
    var s = await app.SnapshotAsync();
    return new
    {
        SessionStatus = s.State.Session?.Status.ToString(),
        OperationStatus = s.State.Lanes.GetValueOrDefault("left")?.Status.ToString(),
        RequestStatus = s.Requests.Single(x => x.Id == requestId).Status.ToString(),
        s.AcceptedQuantity,
        s.RuntimeFaults,
        Blocks = s.State.Blocks.Select(x => new { x.Code, x.Source, x.SourceEpoch }).ToArray(),
        RecoveryRequestIds = s.Recovery.Select(x => x.Request.Id).ToArray(),
        s.Admit
    };
}

using (var store = new SupplyStore(database))
{
    await using (var app = new StationApplication(store, config, new SimulationRequestContract(config)))
    {
        await Bootstrap(app, "original");
        await Command(app, "OpenContext", value: "000001");
        await AcceptNext(app, RequestKind.Context);
        await app.ReceiveAsync(new("audit-enter", "AUDIT-CAM-L", "left-original", "audit-source-enter", 1,
            "audit-admission", "Enter", [], null, DateTimeOffset.UtcNow));
        await app.ReceiveAsync(new("audit-read", "AUDIT-CAM-L", "left-original", "audit-source-read", 2,
            "audit-admission", "Read", ["000099"], 1, DateTimeOffset.UtcNow));
        await AcceptNext(app, RequestKind.Validate);
        var operation = (await app.SnapshotAsync()).State.Lanes["left"];
        await Command(app, "Submit", owner: operation.Id);
        var sent = await AcceptNext(app, RequestKind.Submit);
        requestId = sent.Request.Id;
        acceptedProjection = await Projection(app);
        // Request ID/hash/quantity are unchanged; the same evidence ID now asserts
        // Rejected. SIMULATION-v1.ValidateResponse accepts this classification.
        var contrary = sent.Response with { Result = "Rejected", Reason = "AUDIT contradictory reuse of the same EvidenceId" };
        await app.CompleteAsync(sent.Request, null, contrary);
        contradictoryProjection = await Projection(app);
    }
    // The application owners have stopped, so these authoritative store reads do
    // not race the application's single SQLite connection.
    var state = store.Load();
    storedAfterContradiction = new
    {
        Request = store.Request(requestId),
        Responses = store.Responses(requestId),
        Audits = store.Audits(store.Request(requestId)!.OwnerId),
        DurableBlocks = state.Blocks,
        DurableOperationStatus = state.Lanes["left"].Status.ToString(),
        AcceptedQuantity = store.AcceptedQuantity(state.Session!.Id)
    };
}
using (var reopenedStore = new SupplyStore(database))
{
    await using var restarted = new StationApplication(reopenedStore, config, new SimulationRequestContract(config));
    await Bootstrap(restarted, "restarted");
    restartedProjection = await Projection(restarted);
}
Console.WriteLine(Wire.Encode(new
{
    Audit = "WP-IPC-PROJECT-CONFORMANCE-AUDIT-01",
    AuditBaselineSha = "8cabf06d3c6d53ba551b65e58a13db6cdcaba16a", VerificationSourceSha = "68cdc36f7821825c1ee9e53d866015be5ecf3044",
    Mode = "SYNTHETIC_APPLICATION_API_NO_NETWORK_NO_DEVICE",
    Database = database,
    RequestId = requestId,
    AcceptedProjection = acceptedProjection,
    AfterContradictorySameEvidence = contradictoryProjection,
    StoredAfterContradiction = storedAfterContradiction,
    RestartedAfterFreshSyntheticIdentityAndHealth = restartedProjection
}));
