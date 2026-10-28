using System.Collections.Concurrent;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Drivers.Hikrobot;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task MvCodeReaderFlow(string workspace, string root)
    {
        await using var peer = new Peer();
        var seed = HeartbeatConfiguration(workspace, Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" });
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-idmvs", Parameters = new()
            { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-READER-" + i,
              ["acquisition.mode"] = "continuous", ["entity.mode"] = "read-result" } }).ToArray();
        seed = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, DeferredConnections = [], Login = null };
        var config = Path.Combine(root, "mvreader-config"); Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(seed));
        var launch = new BusinessLaunch(config, Path.Combine(root, "mvreader-data"), null, null);
        var factory = new MvReaderFactory(new ParcelCapabilityFactory(launch));
        // Same isolated Simulation preflight seam used by P0SimulationFactory tests.
        // No Windows policy, real SDK, production state or device is touched.
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory)
            { RuntimeIdentityPreflight = () => { if (seed.Mode != RunMode.Simulation) throw new InvalidOperationException("Synthetic flow only"); } };
        await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
        await Wait(app, s => s.Positions!.All(p => p.Admit));
        var left = factory.Apis[readers[0].DeviceId]; var right = factory.Apis[readers[1].DeviceId];
        int Posts() => peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal));
        left.Emit(0, 1, "SYNTH-0001");
        left.Emit(0, 2, "SYNTH-0001");
        left.Emit(0, 3, "SYNTH-0001");
        var first = await Wait(app, s => s.Operations.Length == 3 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        var original = first.Operations[0]; var frozen = Wire.Encode(original.Wcs!.Request);
        Check(Posts() == 3 && original.ReaderBinding!.DeviceId == readers[0].DeviceId, "real reader pipeline routes SDK candidates into Application/SQLite and three independent RCS requests for a burst of identical decoded codes");
        left.Emit(0, 1, "SYNTH-0001");
        await Wait(app, s => s.Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Duplicate));
        Check(Posts() == 3, "exact redelivery of one SDK result retains one request");
        left.Emit(0, 4, "SYNTH-0001");
        await Wait(app, s => s.Operations.Length == 4 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        right.Emit(0, 1, "SYNTH-0001");
        var completed = await Wait(app, s => s.Operations.Length == 5 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        Check(Posts() == 5 && completed.Operations.Select(o => o.AdmissionId).Distinct().Count() == 5 && completed.Operations.Any(o => o.PositionId == "right"), "each distinct recognized frame, including the same barcode and the other camera, makes an independent RCS request");
        Check(Wire.Encode(completed.Operations.Single(o => o.Id == original.Id).Wcs!.Request) == frozen, "subsequent scans preserve the original frozen request bytes");
        left.Frame!(new(0, 5, 0, 0, 0, 0, []));
        await Wait(app, s => s.Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Rejected));
        Check(Posts() == 5, "no-code callback is recorded as rejected input without a fabricated task");
        await app.StopAsync(default);
        var before = Posts(); left.Emit(4, 5, "SYNTH-LATE");
        Check(Posts() == before && factory.Apis.Values.All(a => a.Calls.Contains("destroy")), "late native callback cannot create a task after controlled shutdown");
        Console.WriteLine("MVCODEREADER_FLOW_PASS synthetic native port, real reader/application/SQLite/HTTP; physical camera and field RCS NOT_RUN");
    }
    sealed class MvReaderFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public Dictionary<string, MvReaderApi> Apis { get; } = [];
        static ParcelConfiguration ReferenceReaders(ParcelConfiguration c)
        {
            var readers = ParcelBindings.Readers(c).Select(r => r with { Driver = "manual", Parameters = null }).ToArray();
            return c with { Reader = readers.Single(r => r.DeviceId == c.Reader.DeviceId), Topology = c.Topology! with { Readers = readers } };
        }
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c)
        {
            if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Synthetic native factory only");
            foreach (var reader in ParcelBindings.Readers(c)) BundledHikrobot.ValidateBinding(reader);
            ParcelCapabilityFactory.ValidateHikrobotIdentities(c);
            inner.Validate(ReferenceReaders(c));
        }
        public CapabilityRequirements Requirements(CapabilityBinding binding) => inner.Requirements(binding);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var ports = inner.Create(ReferenceReaders(c));
            foreach (var source in (ports.Readers?.Values ?? [ports.Reader]).Distinct()) source.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Dictionary<string, IControlledObservationSource> sources = [];
            var hashes = new Dictionary<string, string>(ports.ImplementationHashes);
            foreach (var binding in ParcelBindings.Readers(c))
            {
                var api = new MvReaderApi(binding); Apis.Add(binding.DeviceId, api);
                sources.Add(binding.DeviceId, new HikrobotReader(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api, _ => new Noop()));
                hashes[binding.DeviceId] = Wire.FileHash(typeof(BundledHikrobot).Assembly.Location);
            }
            return ports with { Reader = sources[c.Reader.DeviceId], Readers = sources, ImplementationHashes = hashes };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class Noop : IDisposable { public void Dispose() { } }
    sealed class MvReaderApi(DeviceBinding binding) : IHikrobotApi
    {
        public ConcurrentQueue<string> Calls = new(); public Action<HikFrame>? Frame; uint mode;
        public HikDevice[] Enumerate() => [new(binding.Parameters!["device.ip"], binding.Parameters["device.serial"], "SYNTHETIC", "SYNTHETIC", [])];
        public void Create(HikDevice device) => Calls.Enqueue("create");
        public void Open() => Calls.Enqueue("open");
        public uint TriggerMode() => mode;
        public uint TriggerSource() => throw new InvalidOperationException("No PLC or camera Line source in this fixture");
        public void ConfigureAcquisition(string profile) { mode = profile == "external-trigger" ? 1u : 0u; Calls.Enqueue("configure"); }
        public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding) => Frame = frame;
        public void Emit(uint trigger, uint frame, string code) => Frame!(new(trigger, frame, 0, 0, 0, 0, [code]));
        public void Start() => Calls.Enqueue("start");
        public void Stop() => Calls.Enqueue("stop");
        public void Close() => Calls.Enqueue("close");
        public void Destroy() => Calls.Enqueue("destroy");
    }
}
