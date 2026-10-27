using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class CurrentParameterAuthorityChecks
{
    public static async Task Run(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var login = new CapabilityBinding("login", "reference-login", "1.0.0", "operator-spare", "reference://operator", "PARCEL-REFERENCE-v1", new() { ["subjects"] = "operator" });
        config = config with { CapabilityInstances = ParcelBindings.All(config).Append(login).ToArray() };
        var folder = Path.Combine(root, "config"); Directory.CreateDirectory(folder); ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(root, "data"), null, null);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default);
        var ordinary = await app.SnapshotAsync();
        Check(ordinary.Configuration.Effective.CapabilityInstances is null, "ordinary snapshot omits editable inventory");
        var vm = new PinnedOperatorProjection(app, ordinary); await vm.RefreshAsync();
        var fixedNow = DateTimeOffset.UtcNow;
        LocalManagementMode.ForRoot(launch.DataRoot).Clock = () => fixedNow;
        await ManagementModeFixture.Authorize(app);
        await vm.LoadCapabilitiesAsync();
        var candidate = vm.BuildStructuredConfiguration();
        Check(vm.Snapshot!.Configuration.Effective.CapabilityInstances is null && candidate.CapabilityInstances!.Any(x => x.InstanceId == login.InstanceId && x.Target == login.Target) &&
            candidate.Wcs!.Target == config.Wcs!.Target && candidate.Login is null,
            "authorized editor reads complete application configuration even when UI projection remains redacted; optional login is not silently bound");

        var proxy = DispatchProxy.Create<IManagementParcelFixture, HeldCatalogue>(); var held = (HeldCatalogue)(object)proxy; held.Inner = app;
        var pendingVm = new ParcelViewModel(proxy, "合成参数授权", "synthetic", "sportmaster");
        var pending = pendingVm.LoadCapabilitiesAsync(); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var before = await app.ManagementModeStatusAsync();
            await app.ExitManagementModeAsync(); await ManagementModeFixture.Authorize(app);
            var after = await app.ManagementModeStatusAsync();
            Check(before.ExpiresAt == after.ExpiresAt && before.SessionId is not null && after.SessionId is not null && before.SessionId != after.SessionId,
                "distinct grants remain distinct even with identical expiry timestamps");
        }
        finally { held.Release.TrySetResult(); }
        var refused = false;
        try { await pending; } catch (UnauthorizedAccessException) { refused = true; }
        Check(refused && !pendingVm.HasLoadedConfiguration && pendingVm.CapabilityDrafts.Count == 0,
            "a delayed catalogue cannot publish an editor under a different management grant");
    }
    sealed class PinnedOperatorProjection(IParcelApplication app, ParcelSnapshot ordinary) : ParcelViewModel(app, "合成旧投影", "synthetic", "sportmaster")
    { protected override Task<ParcelSnapshot> ReadSnapshotAsync() => Task.FromResult(ordinary); }
    public class HeldCatalogue : DispatchProxy
    {
        public IParcelApplication Inner = null!;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == nameof(IParcelApplication.CapabilitiesAsync) ? ReadCatalogue() : method.Invoke(Inner, args);
        async Task<CapabilityCatalog> ReadCatalogue()
        { var catalogue = await Inner.CapabilitiesAsync(); Entered.TrySetResult(); await Release.Task; return catalogue; }
    }
    static void Check(bool ok, string text)
    { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PARAM-AUTH " + text); }
}
