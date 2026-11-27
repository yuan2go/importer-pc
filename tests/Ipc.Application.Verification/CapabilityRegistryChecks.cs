using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;

internal static class CapabilityRegistryChecks
{
    static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS REGISTRY " + description);
    }

    static void Reject<TException>(Action action, string description) where TException : Exception
    {
        try { action(); }
        catch (TException) { Check(true, description); return; }
        throw new InvalidOperationException("Expected rejection: " + description);
    }

    static BusinessLaunch Launch(string root) => new(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null);

    static ParcelConfiguration Config(string workspace, ReaderProvider left, ReaderProvider right, WcsProvider wcs)
    {
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var original = seed.Topology!.Readers;
        var readers = new[]
        {
            original[0] with { Driver = left.DriverId, DeviceId = "registry.left." + Wire.Id() },
            original[1] with { Driver = right.DriverId, DeviceId = "registry.right." + Wire.Id() }
        };
        var binding = seed.Wcs! with { Implementation = wcs.Descriptor.Implementation, Contract = wcs.Descriptor.Contract, Parameters = [] };
        return seed with
        {
            Reader = readers[0], Wcs = binding,
            Topology = seed.Topology with
            {
                Readers = readers,
                Positions = seed.Topology.Positions.Select((position, i) => position with { ReaderDeviceId = readers[i].DeviceId }).ToArray()
            }
        };
    }

    internal static async Task Routing(string workspace, string root)
    {
        var left = new ReaderProvider("synthetic-reader-a");
        var right = new ReaderProvider("synthetic-reader-b");
        var wcs = new WcsProvider();
        var unapproved = new ReaderProvider("unapproved-reader") { AllowedInBuiltInOnlyMode = false };
        var registry = new BuiltInCapabilityRegistry([left, right, unapproved, wcs]);
        var config = Config(workspace, left, right, wcs);
        var checks = 0;
        await using var factory = new ParcelCapabilityFactory(Launch(root), registry, ParcelAssembly.CreateCapabilityPolicy())
        {
            RequireProductionManagement = _ => checks++
        };
        Reject<InvalidDataException>(() => registry.RequireReader("unknown-reader"), "unknown reader ID is refused");
        Reject<InvalidDataException>(() => factory.ValidateStructure(config with { Wcs = config.Wcs! with { Implementation = "unknown-wcs" } }),
            "unknown implementation is refused through factory validation");
        Reject<InvalidDataException>(() => factory.ValidateStructure(config with { Wcs = config.Wcs! with { Version = "9.0.0" } }),
            "a registered implementation still requires the exact version");
        Reject<InvalidDataException>(() => factory.ValidateStructure(config with { Extensions = new([], []) }),
            "built-in-only configuration refuses external declarations before opening a store");

        var production = config with { Mode = RunMode.Production };
        Reject<InvalidDataException>(() => factory.ValidateStructure(Config(workspace, unapproved, right, wcs) with { Mode = RunMode.Production }),
            "built-in-only production refuses a registered reader without product approval");
        Check(unapproved.Validated.Count == 0 && unapproved.Created.Count == 0, "an unapproved reader never reaches binding validation or construction");
        var ports = factory.Create(production);
        try
        {
            Check(checks == 1 && factory.AdmissionFailure(production) is null,
                "built-in-only admission checks management authority without needing a qualification file");
            Check(left.Validated.Contains(config.Reader.DeviceId) && right.Validated.Contains(config.Topology!.Readers[1].DeviceId) &&
                wcs.Validated.Contains(config.Wcs!.InstanceId), "assembly validates each binding with its exact registered provider");
            Check(ReferenceEquals(ports.Reader, left.Created.Single()) && ReferenceEquals(ports.Wcs, wcs.Created.Single()) &&
                ports.Readers!.Count == 2, "factory creates the registered reader and WCS ports");
        }
        finally
        {
            foreach (var reader in ports.Readers!.Values) await reader.DisposeAsync();
            await ports.Wcs!.DisposeAsync();
        }
        Check(left.Created.Single().Releases == 1 && right.Created.Single().Releases == 1 && wcs.Created.Single().Releases == 1,
            "the caller can release each transferred port exactly once");
    }

    internal static async Task Discovery(string root)
    {
        var first = new DiscoveryProvider("maker-a", "engine-a", "192.0.2.11") { FailFirstRelease = true };
        var second = new DiscoveryProvider("maker-b", "engine-b", "192.0.2.12");
        var a = new ReaderProvider(first.DriverId) { Discovery = first };
        var b = new ReaderProvider(second.DriverId) { Discovery = second };
        Reject<ArgumentException>(() => _ = new BuiltInCapabilityRegistry([a, a]), "duplicate registered reader IDs are rejected");
        var factory = new ParcelCapabilityFactory(Launch(root), new([a, b]), ParcelAssembly.CreateCapabilityPolicy());
        try
        {
            foreach (var provider in new[] { first, second })
            {
                var discovered = factory.DiscoverCameras(provider.Engine).Single();
                Check(discovered.Ip == provider.Ip && factory.ReadCameraSerial(discovered.Ip, provider.Engine) == provider.DriverId &&
                    provider.Scans == 1 && provider.SerialReads == 1, "camera discovery and serial reads route exactly to " + provider.DriverId);
            }
            Reject<NotSupportedException>(() => factory.DiscoverCameras("unknown-engine"), "an unregistered discovery engine is refused");
            Reject<NotSupportedException>(() => factory.ReadCameraSerial(first.Ip, "unknown-engine"), "an unregistered serial reader engine is refused");
            var error = await SmContractFixture.Failure(() => factory.DisposeAsync().AsTask());
            Check(error is IOException && first.ReleaseAttempts == 1 && second.ReleaseAttempts == 1,
                "one discovery release failure is visible and still allows the other provider to release");
        }
        finally { await factory.DisposeAsync(); }
        Check(first.ReleaseAttempts == 2 && second.ReleaseAttempts == 1, "dispose retries only the original failed discovery owner");
    }

    internal static async Task Live(string workspace, string root)
    {
        foreach (var policyAllowsLive in new[] { false, true })
        {
            var left = new ReaderProvider("reloadable-reader") { SupportsLiveReload = true };
            var right = new ReaderProvider("restart-reader");
            var wcs = new WcsProvider();
            var config = Config(workspace, left, right, wcs);
            var policy = new ProductCapabilityPolicy(config.ApplicationId, builtInOnly: true, supportsExternalExtensions: false, supportsLiveReload: policyAllowsLive);
            await using var factory = new ParcelCapabilityFactory(Launch(Path.Combine(root, policyAllowsLive.ToString())), new([left, right, wcs]), policy);
            var ports = factory.Create(config);
            try
            {
                if (!policyAllowsLive)
                {
                    Check(factory.CreateLive(config, [config.Reader.DeviceId], true) is null && left.Created.Count == 1 && wcs.Created.Count == 1,
                        "disabled live policy stages a restart before constructing replacements");
                    continue;
                }
                var id = config.Reader.DeviceId;
                var other = config.Topology!.Readers[1].DeviceId;
                var live = factory.CreateLive(config, [id], true) ?? throw new InvalidOperationException("Registered live providers should return a candidate.");
                await live.Readers[id].DisposeAsync();
                await live.Wcs!.DisposeAsync();
                Check(left.Created.Count == 2 && wcs.Created.Count == 2 && live.ImplementationHashes[config.Wcs!.InstanceId] == ports.ImplementationHashes[config.Wcs.InstanceId],
                    "supported live providers preserve logical identities and the implementation fingerprint");
                Check(factory.CreateLive(config, [id, other], false) is null && left.Created.Last().Releases == 1 && right.Created.Count == 1,
                    "a nonreloadable reader stages restart and releases an already-created candidate");
                left.FailNextRelease = true;
                var error = await SmContractFixture.Failure(() => Task.Run(() => factory.CreateLive(config, [id, other], false)));
                Check(error is AggregateException && left.Created.Last().Releases == 1, "a failed candidate release remains visible");
            }
            finally
            {
                foreach (var reader in ports.Readers!.Values) await reader.DisposeAsync();
                await ports.Wcs!.DisposeAsync();
            }
            await factory.DisposeAsync();
            Check(left.Created.Last().Releases == 2, "shutdown retries the retained live candidate owner");
        }
    }

    sealed class ReaderProvider(string id) : IBuiltInReaderProvider
    {
        public string DriverId => id;
        public CapabilityDescriptor Descriptor => new("observation", id, "1.0.0", "CONTROLLED-OBSERVATION-v1", [], new());
        public bool IsSimulationOnly => false;
        public bool SupportsLiveReload { get; init; }
        public bool AllowedInBuiltInOnlyMode { get; init; } = true;
        public ICameraDiscoveryProvider? Discovery { get; init; }
        internal bool FailNextRelease { get; set; }
        internal List<string> Validated { get; } = [];
        internal List<ReaderPort> Created { get; } = [];
        public void ValidateBinding(ParcelConfiguration config, DeviceBinding binding) => Validated.Add(binding.DeviceId);
        public IControlledObservationSource Create(DeviceBinding binding)
        {
            var source = new ReaderPort(binding.DeviceId, FailNextRelease);
            FailNextRelease = false;
            Created.Add(source);
            return source;
        }
    }

    sealed class ReaderPort(string id, bool failFirstRelease) : IControlledObservationSource
    {
        public string DeviceId => id;
        public string Epoch => "synthetic";
        public string ConfirmationSource => "synthetic";
        internal int Releases { get; private set; }
        public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health,
            CancellationToken token) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ++Releases == 1 && failFirstRelease ? ValueTask.FromException(new IOException("Fixture reader release failed")) : ValueTask.CompletedTask;
    }

    sealed class WcsProvider : IBuiltInIntegrationProvider
    {
        public CapabilityDescriptor Descriptor { get; } = new("integration", "synthetic-wcs", "1.0.0", "SYNTHETIC-WCS-v1", [], new());
        public bool IsSimulation => false;
        public bool SupportsLiveReload => true;
        internal List<string> Validated { get; } = [];
        internal List<WcsPort> Created { get; } = [];
        public void Validate(ParcelConfiguration config, CapabilityBinding binding, bool requireActiveQualification, DeploymentQualification? qualification) =>
            Validated.Add(binding.InstanceId);
        public IWcsPort Create(ParcelConfiguration config, CapabilityBinding binding)
        {
            var port = new WcsPort(binding.InstanceId);
            Created.Add(port);
            return port;
        }
    }

    sealed class WcsPort(string id) : IWcsPort
    {
        public string InstanceId => id;
        internal int Releases { get; private set; }
        public Task StartAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<CapabilityHealth> HealthAsync(CancellationToken token) => throw new NotSupportedException();
        public Task ReconnectAsync(CancellationToken token) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Releases++; return ValueTask.CompletedTask; }
    }

    sealed class DiscoveryProvider(string id, string engine, string ip) : ICameraDiscoveryProvider
    {
        public string DriverId => id;
        internal string Engine => engine;
        internal string Ip => ip;
        internal int Scans { get; private set; }
        internal int SerialReads { get; private set; }
        internal int ReleaseAttempts { get; private set; }
        internal bool FailFirstRelease { get; init; }
        public bool SupportsEngine(string readerEngine) => readerEngine == engine;
        public DiscoveredCamera[] Discover(string readerEngine)
        {
            if (readerEngine != engine) throw new InvalidOperationException("Wrong discovery route");
            Scans++;
            return [new(ip, id, id, "1", false)];
        }
        public string ReadSerial(string address, string readerEngine)
        {
            if (address != ip || readerEngine != engine) throw new InvalidOperationException("Wrong serial route");
            SerialReads++;
            return id;
        }
        public ValueTask DisposeAsync() => ++ReleaseAttempts == 1 && FailFirstRelease ?
            ValueTask.FromException(new IOException("Fixture discovery release failed")) : ValueTask.CompletedTask;
    }
}
