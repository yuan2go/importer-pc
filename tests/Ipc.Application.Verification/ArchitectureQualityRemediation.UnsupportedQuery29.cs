using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ArchitectureQualityRemediation
{
    internal static async Task VerifyUnsupportedReceiptQuery29(string workspace, string path)
    {
        var password = "OP29-" + Wire.Id();
        var seed = await MissingReceipt(workspace, path, password);
        NoQueryFactory? factory = null;
        await using var app = await Open(seed.Configuration, path, inner => factory = new(inner));
        Check(await app.EnterManagementModeAsync(password), "OP29 missing-raw fixture re-enters its own real management grant");
        await Command(app, "Pause");
        var before = Wire.Encode((await app.OperationAsync(seed.Original.Id))!.Wcs);
        var reply = await Command(app, "ReconcileReceipt", seed.Occurrence, "Inspect original missing receipt without an authoritative query contract");
        using var repo = new ParcelRepository(DirectoryFor(path, seed.Configuration), seed.Configuration.ApplicationId);
        Check(reply.Status == "QueryUnsupported" && factory!.QueryCalls == 0 && factory.ResolveCalls == 0 &&
            Wire.Encode((await app.OperationAsync(seed.Original.Id))!.Wcs) == before &&
            repo.MissingAwaitingRaw(seed.Occurrence) && !repo.HasReceiptOutcomeBasis(seed.Occurrence),
            "OP29 unsupported missing-receipt query preserves the exact original stage and raw responsibility without query, send or fabricated outcome");
    }
    sealed class NoQueryFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public int QueryCalls, ResolveCalls;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding binding) => inner.Requirements(binding) with { Query = false };
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var ports = inner.Create(c);
            return ports with { Wcs = new NoQueryWcs(ports.Wcs!, this) };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class NoQueryWcs(IWcsPort inner, NoQueryFactory owner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
        public Task<CapabilityHealth> HealthAsync(CancellationToken token) => inner.HealthAsync(token);
        public Task ReconnectAsync(CancellationToken token) => inner.ReconnectAsync(token);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token)
        { Interlocked.Increment(ref owner.ResolveCalls); return inner.ResolveSlotAsync(request, token); }
        public Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken token)
        { Interlocked.Increment(ref owner.QueryCalls); throw new InvalidOperationException("A query-less fixture must never be called"); }
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
