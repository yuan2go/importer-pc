using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class MaintenanceVersionBoundary
{
    const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [TestMethod]
    public void BuildDisplayUsesReviewedMediaBytesAndDisclosesMissingEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipc-build-display-" + Wire.Id());
        Directory.CreateDirectory(Path.Combine(root, "desktop"));
        try
        {
            var source = new string('a', 40);
            var file = Path.Combine(root, "desktop", "application-release.json");
            var release = new ApplicationRelease(2, "sportmaster", source, [], [], [], "1.0.0");
            File.WriteAllText(file, Wire.Encode(release));
            var media = new DeploymentMedia(root, "sportmaster", source, new() { ["desktop/application-release.json"] = Wire.FileHash(file) });
            Assert.AreEqual("1.0.0", WindowsDeploymentSetup.MediaBuild(media).BusinessVersion);
            File.WriteAllText(file, Wire.Encode(release with { BusinessVersion = "9.0.0" }));
            var changed = WindowsDeploymentSetup.MediaBuild(media);
            Assert.AreEqual("未核验", changed.BusinessVersion);
            Assert.AreEqual(source, changed.SourceCommit, "The original reviewed source remains identifiable without endorsing the changed target.");
            File.Delete(file);
            Assert.AreEqual("未核验", WindowsDeploymentSetup.MediaBuild(media).BusinessVersion);
            Assert.AreEqual("未核验", WindowsDeploymentSetup.RegisteredBuild(new("sportmaster", source, root, root, root, "synthetic")).BusinessVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("ReviewRollback")]
    [DataRow("Rollback")]
    [DataRow("ReviewVersionPruning")]
    [DataRow("PruneVersions")]
    [DataRow("ResumeVersionPruning")]
    [DataRow("SimpleUninstall")]
    [DataRow("MediaUninstall")]
    public void NewVersionActionRequiresBegin(string action)
        => Assert.IsTrue(ParcelStartupServices.CancellableMaintenance(action), "The real helper dispatch must allocate MaintenanceExecution and await Begin: " + action);

    static async Task<string?> RoundTrip(string status, string? result, string? error = null, int exitCode = 0)
    {
        var terminal = ParcelStartupServices.BoundMaintenanceTerminal(status, Hash, error, result);
        var frames = JsonSerializer.Serialize(new { Status = "Ready", RequestSha256 = Hash }) + "\n" +
            JsonSerializer.Serialize(new { Status = terminal.Status, RequestSha256 = Hash, Error = terminal.Error, Result = terminal.Result }) + "\n";
        using var input = new StringReader(frames);
        var reply = await ParcelStartupServices.ReadBoundMaintenanceResponse(input, Hash, true, _ => { }, _ => { }, () => Task.CompletedTask, CancellationToken.None);
        return ParcelStartupServices.CompleteBoundMaintenanceResponse(reply, exitCode, false);
    }

    [TestMethod]
    public async Task UnknownAndOversizedTerminalRemainUnknown()
    {
        Assert.AreEqual("Unknown", ParcelStartupServices.MaintenanceFailureStatus(new InstallationOutcomeUnknownException("synthetic committed result loss")));
        Assert.AreEqual("Failed", ParcelStartupServices.MaintenanceFailureStatus(new IOException("synthetic known precondition refusal")));
        await Assert.ThrowsExactlyAsync<InstallationOutcomeUnknownException>(() => RoundTrip("Unknown", null, "synthetic interrupted commit", 2));
        var bounded = ParcelStartupServices.BoundMaintenanceTerminal("Completed", Hash, null, new string('x', 100000));
        Assert.AreEqual("Unknown", bounded.Status);
        Assert.IsNull(bounded.Result);
        await Assert.ThrowsExactlyAsync<InstallationOutcomeUnknownException>(() => RoundTrip(bounded.Status, bounded.Result, bounded.Error, 2));
    }

    [TestMethod]
    public async Task VersionReviewUsesBoundedSummary()
    {
        var files = Enumerable.Range(0, 10000).ToDictionary(i => "app/synthetic-file-" + i + ".dll", _ => new string('B', 64));
        var original = new DeploymentPrunePlan("full-server-inventory-identity", Guid.NewGuid().ToString("N"), "sportmaster", Hash, true, "synthetic review",
            [new(@"D:\synthetic-old-version", files)], [new(@"D:\synthetic-current", "current")], true);
        var reviewed = ParcelStartupServices.VersionPruningReviewSummary(new(original, new("business", "PruneVersions", true, "synthetic settled", Hash, @"D:\synthetic-backup.db")));
        Assert.AreEqual(original.Identity, reviewed.Deployment.Identity);
        Assert.AreEqual(0, reviewed.Deployment.Delete.Single().Files.Count);
        Assert.AreEqual(10000, reviewed.Deployment.Delete.Single().ReviewedFileCount);
        Assert.AreEqual(10000, original.Delete.Single().Files.Count, "The complete server inventory must remain unchanged.");
        var result = await RoundTrip("Completed", Wire.Encode(reviewed));
        Assert.AreEqual(original.Identity, Wire.Decode<StartupVersionPruneReview>(result!).Deployment.Identity);
    }

    [TestMethod]
    public async Task VersionReceiptDisclosesDisplayTruncation()
    {
        var remaining = Enumerable.Range(0, 5000).Select(i => @"D:\合成旧版本\待保留文件\" + i + new string('测', 100) + ".txt").ToArray();
        var original = new DeploymentPruneReceipt(Guid.NewGuid().ToString("N"), "Partial", "synthetic locked originals", remaining, @"D:\synthetic-protected-original.json");
        var summary = ParcelStartupServices.VersionPruningReceiptSummary(original);
        Assert.IsTrue(summary.RemainingTruncated);
        Assert.AreEqual(remaining.Length, summary.TotalRemaining);
        Assert.IsTrue(summary.Remaining.Length < remaining.Length);
        Assert.AreEqual(5000, original.Remaining.Length);
        var result = await RoundTrip("Completed", Wire.Encode(summary));
        var actual = Wire.Decode<DeploymentPruneReceipt>(result!);
        Assert.AreEqual(original.OperationId, actual.OperationId);
        Assert.AreEqual(original.OperationFile, actual.OperationFile);
        Assert.AreEqual("Partial", actual.Status);
        Assert.IsTrue(actual.RemainingTruncated);
    }
}
