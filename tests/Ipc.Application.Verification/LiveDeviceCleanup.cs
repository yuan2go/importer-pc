using Ipc.Application.Contracts;
using Ipc.Platform;

static class LiveDeviceCleanup
{
    internal static async Task Run(string workspace, string root)
    {
        List<string> failures = [];
        foreach (var candidateFailure in new[] { false, true })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "cleanup-" + candidateFailure));
            await h.StartAsync();
            var before = await h.App.SnapshotAsync();
            var decoder = h.Factory.Decoders[h.Left];
            if (candidateFailure) { h.Factory.RejectLiveReaders = true; h.Factory.FailCandidateRelease = true; }
            else decoder.FailReleaseAt = decoder.ReleaseCalls + 2; // Stop succeeds; Dispose's second cleanup fails.
            var readers = before.Configuration.Effective.Topology!.Readers.Select(r => r.DeviceId == h.Left
                ? r with { Parameters = new Dictionary<string, string>(r.Parameters!) { ["device.ip"] = "192.0.2.99" } } : r).ToArray();
            var candidate = before.Configuration.Effective with { Reader = readers.Single(r => r.DeviceId == h.Left), Topology = before.Configuration.Effective.Topology with { Readers = readers } };
            h.Factory.Expect(candidate);
            Exception? failure = null;
            BusinessReceipt? receipt = null;
            try { receipt = await h.App.StageConfigurationAsync(before.Configuration.Revision, candidate, "Synthetic cleanup failure"); }
            catch (Exception error) { failure = error; }
            if (failure is null && receipt?.Status != "Rejected") failures.Add("Release failure reported successful save: candidate=" + candidateFailure);
            if ((await h.App.SnapshotAsync()).Configuration.Revision != before.Configuration.Revision)
                failures.Add("Release failure changed the configuration: candidate=" + candidateFailure);
            if (failures.Count == 0) Console.WriteLine("PASS CLEANUP release failure is visible and retains the original configuration: candidate=" + candidateFailure);
            var failed = candidateFailure ? h.Factory.Decoders[h.Left] : decoder;
            var calls = failed.ReleaseCalls;
            failed.FailReleaseAt = calls + 1;
            try { await h.App.DisposeAsync(); failures.Add("Repeated cleanup failure reported successful shutdown: candidate=" + candidateFailure); }
            catch (AggregateException) { }
            if (!h.App.HasUnreleasedResources) failures.Add("Repeated cleanup failure released application ownership: candidate=" + candidateFailure);
            if (failures.Count == 0) Console.WriteLine("PASS CLEANUP repeated release failure keeps shutdown retryable and application ownership retained: candidate=" + candidateFailure);
            await h.App.DisposeAsync();
            if (failed.ReleaseCalls <= calls || h.App.HasUnreleasedResources)
                failures.Add("Shutdown lost the failed owner: candidate=" + candidateFailure);
            if (failures.Count == 0) Console.WriteLine("PASS CLEANUP shutdown retries the failed owner and then releases application ownership: candidate=" + candidateFailure);
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
}
