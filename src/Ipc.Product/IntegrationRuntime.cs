using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
namespace Ipc.Product;

// Product assembly selects implementations. Business requests retain the exact selection;
// routing never consults a changed endpoint, current dropdown or default provider.
sealed class IntegrationRuntime(StationConfig configuration, ExtensionStore? store) : IRequestContract, IIntegrationPort
{
    sealed class Entry(IntegrationImplementation identity, IIntegrationPort port, IRequestContract codec,
        ExtensionLoading? loading = null, PackageUseLease? lease = null)
    {
        public IntegrationImplementation Identity = identity;
        public IIntegrationPort Port = port;
        public IRequestContract Codec = codec;
        public ExtensionLoading? Loading = loading;
        public PackageUseLease? Lease = lease;
        public Task? Stop, Disposal;
        public bool Disposed;
        public string? CurrentPort;
        public readonly string Epoch = Wire.Id();
        public long Sequence;
        public IntegrationHealthFact? Health;
        public Task? Probe;
        public string? QueryFailure;
    }
    readonly object gate = new();
    readonly Dictionary<string, Entry> entries = [];
    readonly CancellationTokenSource stopping = new();
    TaskCompletionSource drained = Completed();
    int active;
    bool disposed;
    Entry? current;
    static TaskCompletionSource Completed() { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }
    static string Fingerprint(IntegrationImplementation identity) => Wire.Hash(Wire.Encode(identity with { ConfigurationSha256 = "" }));
    static IntegrationImplementation Seal(IntegrationImplementation identity) => identity with { ConfigurationSha256 = Fingerprint(identity) };
    public IntegrationDiagnostic[] Diagnostics
    {
        get
        {
            lock (gate) return entries.Values.Select(x =>
            {
                var lifecycle = x.Port is IControlledIntegration controlled ? controlled.Diagnostic :
                    new IntegrationDiagnostic(x.Identity.InstanceId, disposed ? "Disposed" : stopping.IsCancellationRequested ? "Stopping" : "Running", active, null);
                var health = x.Health;
                var state = x.CurrentPort is null ? "HistoricalQuery" : health is null ? "Unconfirmed" : health.ValidUntil <= DateTimeOffset.UtcNow ? "Expired" : health.Status.ToString();
                return new IntegrationDiagnostic(x.Identity.InstanceId, state, lifecycle.InFlight, x.QueryFailure ?? health?.Failure ?? lifecycle.LastFailure)
                { Target = x.Identity.Target, DeploymentVersion = x.Identity.DeploymentVersion, ConfigurationSha256 = x.Identity.ConfigurationSha256,
                    Port = x.CurrentPort, Historical = x.CurrentPort is null, Lifecycle = lifecycle.State, Health = health };
            }).ToArray();
        }
    }

    public void Initialize()
    {
        lock (gate)
        {
            if (current is not null) throw new InvalidOperationException("Integration runtime is single-use");
            foreach (var service in configuration.Extensions?.Services?.Where(x => x.Enabled) ?? [])
            {
                var binding = configuration.Extensions!.Ports!.Single(x => x.InstanceId == service.Id);
                var package = configuration.Extensions.Packages.Single(x => x.Id == service.PackageId);
                var manifest = store!.VerifyInstalled(package).Manifest;
                var module = manifest.Modules.Single(x => x.Id == service.ModuleId && x.Kind == "integration");
                var parameters = module.Parameters.Where(x => service.Parameters.ContainsKey(x.Key) || x.Default is not null)
                    .ToDictionary(x => x.Key, x => service.Parameters.GetValueOrDefault(x.Key) ?? x.Default!);
                var identity = Seal(new("extension", package, module.Id, service.Id, configuration.Version, "",
                    manifest.PublicContracts!["Ipc.Supply.Contracts"], binding.Target, configuration.TimeoutMs, configuration.Policy, configuration.FixtureDemand, parameters));
                var entry = Resolve(identity); entry.CurrentPort = binding.Port;
                if (binding.Port == "requests") current = entry;
            }
            if (current is null)
            {
                if (configuration.Extensions?.Services?.Any(x => x.Enabled) == true) throw new InvalidDataException("Requests integration binding is missing");
                current = Resolve(Seal(new("builtin.reference", null, "simulation-v1", "builtin.reference", configuration.Version, "",
                    Wire.FileHash(typeof(SimulationRequestContract).Assembly.Location), configuration.Integration, configuration.TimeoutMs, configuration.Policy, configuration.FixtureDemand, [])));
            }
            current.CurrentPort = "requests";
        }
    }
    Entry Resolve(IntegrationImplementation identity)
    {
        if (identity.ConfigurationSha256 != Fingerprint(identity) || identity.Target.Contract != "SIMULATION-v1" || configuration.Mode != RunMode.Simulation)
            throw new InvalidDataException("OriginalIntegrationUnavailable: frozen implementation/configuration/contract mismatch");
        var key = identity.ConfigurationSha256;
        if (entries.TryGetValue(key, out var existing)) return existing;
        if (identity.Driver == "builtin.reference" && identity.Package is null)
        {
            if (identity.ContractSha256 != Wire.FileHash(typeof(SimulationRequestContract).Assembly.Location))
                throw new InvalidDataException("OriginalIntegrationUnavailable: builtin implementation bytes changed");
            var original = configuration with { Extensions = null, Integration = identity.Target, TimeoutMs = identity.TimeoutMs, Policy = identity.Policy, FixtureDemand = identity.FixtureDemand };
            var result = new Entry(identity, new LoopbackIntegration(original), new SimulationRequestContract(original));
            entries.Add(key, result); return result;
        }
        if (identity.Driver != "extension" || identity.Package is null || store is null)
            throw new InvalidDataException("OriginalIntegrationUnavailable: exact extension store/identity missing");
        // Pin the entire transitive dependency closure, including historical query-only packages.
        List<PackageReference> closure = [];
        void Visit(PackageReference reference)
        {
            if (closure.Contains(reference)) return;
            closure.Add(reference);
            foreach (var dependency in store.VerifyInstalled(reference).Manifest.Dependencies) Visit(dependency);
        }
        Visit(identity.Package);
        var lease = store.AcquireUse(closure); var loading = new ExtensionLoading(store);
        try
        {
            var manifest = store.VerifyInstalled(identity.Package).Manifest;
            var module = manifest.Modules.SingleOrDefault(x => x.Id == identity.ModuleId && x.Kind == "integration")
                ?? throw new InvalidDataException("OriginalIntegrationUnavailable: typed integration module missing");
            if (manifest.PublicContracts?.GetValueOrDefault("Ipc.Supply.Contracts") != identity.ContractSha256 ||
                ExtensionSchema.Validate(module.Parameters, identity.Parameters).Length != 0)
                throw new InvalidDataException("OriginalIntegrationUnavailable: frozen public contract/parameters changed");
            var factory = loading.CreateFactory<IIntegrationExtension>(identity.Package, module, "integration");
            var port = factory.Create(new(identity.InstanceId, RunMode.Simulation, identity.Target, identity.TimeoutMs, identity.Policy, identity.FixtureDemand, identity.Parameters))
                ?? throw new InvalidDataException("Integration factory returned null");
            var entry = new Entry(identity, port, port, loading, lease);
            entries.Add(key, entry); // Own the returned port before validating its identity.
            if (port.InstanceId != identity.InstanceId || port.Contract != identity.Target.Contract)
                throw new InvalidDataException("Returned integration identity/capability mismatch; ownership retained");
            return entry;
        }
        catch
        {
            if (!entries.ContainsKey(key)) { loading.Dispose(); lease.Dispose(); }
            throw;
        }
    }
    Entry Original(BusinessRequest request)
    {
        if (request.Implementation is { } identity)
        {
            if (identity.Target != request.Binding) throw new InvalidDataException("OriginalIntegrationUnavailable: target identity changed");
            return Resolve(identity);
        }
        // Explicit legacy adapter: old SIMULATION-v1 persisted the policy/demand in the
        // original payload but no implementation field. Never borrow the current target.
        var payload = Wire.Decode<FixturePayload>(request.Payload);
        return Resolve(Seal(new("builtin.reference", null, "simulation-v1", "legacy.contract1", "legacy-unrecorded", "",
            Wire.FileHash(typeof(SimulationRequestContract).Assembly.Location), request.Binding, configuration.TimeoutMs, payload.Policy, payload.InitialDemand, [])));
    }
    public IntegrationImplementation Freeze()
    {
        lock (gate) return Wire.Decode<IntegrationImplementation>(Wire.Encode((current ?? throw new InvalidOperationException("Integration not initialized")).Identity));
    }
    public string Encode(RequestIntent intent, string scenario) { lock (gate) return current!.Codec.Encode(intent, scenario); }
    public RequestAuthority Authority(BusinessRequest request) { lock (gate) return Original(request).Codec.Authority(request); }
    public void ValidateResponse(BusinessRequest request, IntegrationResponse response) { lock (gate) Original(request).Codec.ValidateResponse(request, response); }
    async Task<T> Invoke<T>(Func<Task<T>> action)
    {
        lock (gate)
        {
            if (disposed || stopping.IsCancellationRequested) throw new InvalidOperationException("Integration runtime is stopping");
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await action().ConfigureAwait(false); }
        finally { lock (gate) if (--active == 0) drained.TrySetResult(); }
    }
    public Task<IntegrationResponse> ExecuteAsync(BusinessRequest request, CancellationToken token) => Invoke(async () =>
    {
        Entry entry; lock (gate) entry = Original(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token); linked.CancelAfter(entry.Identity.TimeoutMs);
        return await entry.Port.ExecuteAsync(request, linked.Token).ConfigureAwait(false);
    });
    public Task<IntegrationResponse?> QueryAsync(BusinessRequest request, CancellationToken token) => Invoke(async () =>
    {
        Entry entry; lock (gate) entry = Original(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token); linked.CancelAfter(entry.Identity.TimeoutMs);
        try
        {
            var result = await entry.Port.QueryAsync(request, linked.Token).ConfigureAwait(false);
            lock (gate) entry.QueryFailure = result is null ? "No authoritative original record or query unsupported" : null;
            return result;
        }
        catch (Exception ex) { lock (gate) entry.QueryFailure = "Original query: " + ex.GetType().Name + ": " + ex.Message; throw; }
    });
    // Each configured instance owns one probe. Slow auxiliary ports do not serialize
    // the sender or renew the primary timestamp; historical query-only ports never join.
    public Task<IntegrationHealthFact[]> RefreshHealthAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (disposed || stopping.IsCancellationRequested) throw new OperationCanceledException("Integration stopping");
            foreach (var entry in entries.Values.Where(x => x.CurrentPort is not null))
                if (entry.Probe is not { IsCompleted: false } && (entry.Health is null || entry.Health.ConfirmedAt < DateTimeOffset.UtcNow.AddSeconds(-1)))
                    entry.Probe = Probe(entry, token);
            return Task.FromResult(entries.Values.Where(x => x.CurrentPort is not null && x.Health is not null).Select(x => x.Health!).ToArray());
        }
    }
    Task Probe(Entry entry, CancellationToken token) => Invoke(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token); linked.CancelAfter(entry.Identity.TimeoutMs);
        var sequence = ++entry.Sequence;
        bool healthy = false; string? failure = null;
        try
        {
            healthy = await entry.Port.HealthAsync(linked.Token).ConfigureAwait(false);
            if (!healthy) failure = (entry.Port as IControlledIntegration)?.Diagnostic.LastFailure ?? "Health endpoint rejected contract or reported unavailable";
        }
        catch (Exception ex) { failure = ex.GetType().Name + ": " + ex.Message; }
        lock (gate)
        {
            if (!stopping.IsCancellationRequested && sequence == entry.Sequence)
            {
                var now = DateTimeOffset.UtcNow;
                entry.Health = new(entry.Identity.InstanceId, entry.CurrentPort!, entry.Identity.DeploymentVersion, entry.Identity.ConfigurationSha256,
                    entry.Identity.Target, entry.Epoch, sequence, healthy ? ServiceStatus.Ready : ServiceStatus.Unavailable,
                    now, now.AddMilliseconds(entry.Identity.TimeoutMs + 3000), failure);
            }
        }
        return healthy;
    });
    public async Task<bool> HealthAsync(CancellationToken token)
    {
        var facts = await RefreshHealthAsync(token).ConfigureAwait(false);
        return facts.SingleOrDefault(x => x.Port == "requests") is { } primary && primary.Status == ServiceStatus.Ready && primary.ValidUntil > DateTimeOffset.UtcNow;
    }
    public async Task StopAsync(CancellationToken token)
    {
        Task drain; Entry[] ports;
        lock (gate) { stopping.Cancel(); drain = drained.Task; ports = entries.Values.ToArray(); }
        List<Exception> errors = [];
        foreach (var entry in ports.Reverse())
            try
            {
                if (entry.Stop is null || entry.Stop.IsFaulted || entry.Stop.IsCanceled) entry.Stop = Task.Run(() => entry.Port is IControlledIntegration controlled ? controlled.StopAsync(CancellationToken.None) : Task.CompletedTask);
                await entry.Stop.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (Exception ex) { errors.Add(ex); }
        try { await drain.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("Integration stop incomplete; exact ports/packages/leases retained", errors);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var entry in entries.Values.Reverse())
        {
            if (entry.Disposed) continue;
            if (entry.Disposal is null || entry.Disposal.IsFaulted || entry.Disposal.IsCanceled) entry.Disposal = Task.Run(async () => await entry.Port.DisposeAsync().ConfigureAwait(false));
            await entry.Disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            entry.Loading?.Dispose(); entry.Lease?.Dispose(); entry.Disposed = true;
        }
        disposed = true;
    }
}
