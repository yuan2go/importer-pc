using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

using static SmContractFixture;

internal static class CameraReplacementGuards
{
    public static async Task LiveDevices(string workspace, string root)
    {
        foreach (var phase in new[] { "start-audit", "configuration-write" })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, phase));
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            var before = await h.WaitForLiveConfigurationWindowAsync();
            var originalPorts = Field<ParcelPorts>(h.App, "ports");
            var originalDecoder = h.Factory.Decoders[h.Left];
            var originalRight = h.Factory.Sources[h.Right];
            var directory = DirectoryFor(h.Launch, h.Config);
            var ledger = Path.Combine(directory, "configuration.json"); var originalHash = Wire.FileHash(ledger);
            var readers = before.Configuration.Effective.Topology!.Readers.Select(r => r.DeviceId == h.Left
                ? r with { Parameters = new(r.Parameters!) { ["device.ip"] = "192.0.2.99" } } : r).ToArray();
            var candidate = before.Configuration.Effective with { Reader = readers[0], Topology = before.Configuration.Effective.Topology with { Readers = readers } };
            h.Factory.Expect(candidate);
            var repository = Field<ParcelRepository>(h.App, "repository");
            FileStream? held = null;
            try
            {
                if (phase == "start-audit") repository.Boundary = name =>
                { if (name == "BeforeBusinessCommit:LiveDeviceApplyStarted") throw new InvalidOperationException("Synthetic live-apply audit failure"); };
                else held = new FileStream(ledger, FileMode.Open, FileAccess.Read, FileShare.Read);
                BusinessReceipt? receipt = null;
                try { receipt = await h.App.StageConfigurationAsync(before.Configuration.Revision, candidate, "CR-SM live replacement failure"); }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { }
                Check(receipt?.Status != "Effective" && Wire.FileHash(ledger) == originalHash &&
                    (await h.App.SnapshotAsync()).Configuration.Revision == before.Configuration.Revision,
                    phase + " retains the exact original authoritative configuration");
                Check(ReferenceEquals(Field<ParcelPorts>(h.App, "ports").Reader, originalPorts.Reader) &&
                    ReferenceEquals(h.Factory.Sources[h.Right], originalRight) && h.Factory.Decoders[h.Left].ReleaseCalls > 0,
                    phase + " never adopts the uncommitted candidate, releases it and preserves the other camera");
                var fences = Field<System.Collections.Concurrent.ConcurrentDictionary<string, byte>>(h.App, "maintainedResources");
                Check(phase == "start-audit" ? !fences.ContainsKey(h.Left) && originalDecoder.ReleaseCalls == 0 : fences.ContainsKey(h.Left),
                    "pre-stop failure releases the temporary fence; post-stop failure retains affected-device isolation");
            }
            finally { repository.Boundary = null; held?.Dispose(); }
            await h.App.DisposeAsync();
            Check(!h.App.HasUnreleasedResources, phase + " retains all failed owners through real shutdown cleanup");
        }
    }
}
