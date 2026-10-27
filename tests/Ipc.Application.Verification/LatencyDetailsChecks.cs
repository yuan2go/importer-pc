using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class LatencyDetailsChecks
{
    public static async Task Run(string workspace, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
        var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
        ParcelOperation Make(string position, string reason) => new(Wire.Id(), Wire.Id(), "SYNTHETIC", Wire.Id(), DateTimeOffset.UtcNow, Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null,
            ParcelState.Abandoned, null, null, null, reason, PositionId: position);
        var older = Make("left", "synthetic historical cancelled intention");
        var request = new FrozenRequest(Wire.Id(), c.ApplicationId, older.Id, older.AdmissionId, "wcs-slot", c.Wcs!, "synthetic-only", older.ConfigurationHash, "synthetic-secret-payload", Wire.Hash("synthetic-secret-payload"), DateTimeOffset.UtcNow);
        older = older with { Wcs = new(request, StageState.Cancelled, 0, []) };
        var large = Make("left", new string('R', 1024 * 1024)); var small = Make("right", "synthetic small");
        await gate.WaitAsync();
        try
        {
            repo.Commit(older, "SyntheticUnsent", "No external attempt"); repo.Archive([older.Id], "Synthetic archived original");
            repo.Commit(large, "SyntheticLarge", "Performance-only text"); repo.Commit(small, "SyntheticSmall", "Performance-only text");
        }
        finally { gate.Release(); }
        var vm = new ParcelViewModel(runtime, "synthetic details", "sportmaster");
        var formats = 0; var getters = 0;
        vm.DetailMeasurement = () => getters++;
        vm.BeforeDetailsFormatForVerification = () => { Interlocked.Increment(ref formats); return Task.CompletedTask; };
        await vm.RefreshAsync(); vm.Selected = vm.Operations.Single(x => x.Id == large.Id); await vm.DrainAsync();
        Check(formats == 0, "hidden details do not generate a large string");
        vm.SetDetailsVisibility(true, true); await vm.DrainAsync();
        Check(vm.Details.Length < 66000 && vm.Details.Contains("截断") && vm.StructuredDetails.Length < 66000, "large raw and structured display are explicitly bounded");
        var complete = await vm.FullDetailsAsync();
        Check(complete.Length > 1024 * 1024 && Wire.Decode<ParcelOperation>(complete).Id == large.Id, "explicit full copy retains the complete local original beyond display limit");
        getters = 0; var warmFormats = formats;
        for (var i = 0; i < 5; i++) await vm.RefreshAsync();
        await vm.DrainAsync();
        Check(getters == 0 && formats == warmFormats, "unchanged job refresh neither evaluates Details nor formats it again");
        // An explicit replacement selection is a new read owner even when its
        // synthetic projection preserves the durable request/reply identities.
        var selectedReply = new StageReply(Wire.Id(), request.Id, request.PayloadHash, false, null, null, null, null, "synthetic-projection-one", DateTimeOffset.UtcNow);
        var selectedProjection = older with { Wcs = older.Wcs! with { Replies = [selectedReply] } };
        vm.Selected = selectedProjection; await vm.DrainAsync();
        vm.Selected = selectedProjection with { Wcs = selectedProjection.Wcs! with { Replies = [selectedReply with { Evidence = "synthetic-projection-two" }] } };
        await vm.DrainAsync();
        Check(vm.StructuredDetails.Contains("synthetic-projection-two"), "explicit replacement selection cannot retain an earlier same-ID projection");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.BeforeDetailsFormatForVerification = () => { entered.TrySetResult(); return release.Task; };
        vm.Selected = small; await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); vm.Selected = large;
        vm.BeforeDetailsFormatForVerification = null; release.TrySetResult(); await vm.DrainAsync();
        Check(vm.StructuredDetails.Contains(large.Id) && !vm.StructuredDetails.Contains(small.Id), "late text for a previous selection cannot replace the new owner");
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.BeforeDetailsFormatForVerification = () => { entered.TrySetResult(); return release.Task; };
        vm.Selected = older; await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.ExitManagementModeAsync(); await vm.RefreshAsync();
        vm.BeforeDetailsFormatForVerification = null; release.TrySetResult(); await vm.DrainAsync();
        Check(vm.Selected?.Id == older.Id && vm.Selected.Wcs!.Request.Payload == "" && !vm.StructuredDetails.Contains(c.Wcs!.Target) && !vm.Details.Contains("synthetic-secret-payload"),
            "revoked authority re-reads a historical selection and fences earlier privileged formatting");
        var copyRefused = false;
        try { _ = await vm.FullDetailsAsync(); } catch (UnauthorizedAccessException) { copyRefused = true; }
        Check(copyRefused, "full local original copy requires current management authority even when invoked directly");
    }
}
