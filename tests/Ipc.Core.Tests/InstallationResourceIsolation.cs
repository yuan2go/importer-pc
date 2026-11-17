using System.Net;
using System.Text.Json.Nodes;
using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass, DoNotParallelize]
public sealed class InstallationResourceIsolation
{
    string root = null!;
    DeploymentLocation peer = null!;
    Func<DeploymentLocation?>? originalReader;
    [TestInitialize]
    public void Prepare()
    {
        root = Path.Combine(Path.GetTempPath(), "ipc-installation-boundary-" + Guid.NewGuid().ToString("N"));
        peer = new("sportmaster", new string('a', 40), Path.Combine(root, "release"), Path.Combine(root, "config"), Path.Combine(root, "data"), "S-1-5-32-545");
        Directory.CreateDirectory(peer.ConfigurationDirectory);
        originalReader = InstallationResourceBoundary.OtherInstallationForVerification;
        InstallationResourceBoundary.OtherInstallationForVerification = () => peer;
    }
    [TestCleanup]
    public void Restore() => InstallationResourceBoundary.OtherInstallationForVerification = originalReader;

    [TestMethod]
    public void FormalPathsRejectEveryTestAreaAndItsParents()
    {
        foreach (var path in new[] { InstallationIdentity.TestProgramRoot, InstallationIdentity.TestProtectedAnchor,
            InstallationIdentity.TestDataArea, @"D:\ProgramData", @"D:\CainiaoSupplyStationTest\releases", @"D:\ProgramData\ImporterPc\..\ImporterPcTest\data" })
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireOutsideTestStorage(path), path);
        InstallationIdentity.RequireOutsideTestStorage(@"D:\CainiaoSupplyStationTest-other");
        InstallationIdentity.RequireOutsideTestStorage(@"D:\ProgramData\ImporterPc\data");
    }
    [TestMethod]
    public void ExternalFileSelectionCannotCrossInstallationAreas()
    {
        InstallationResourceBoundary.OtherInstallationForVerification = () => null;
        if (InstallationIdentity.IsTest)
        {
            InstallationIdentity.RequireExternalFilePath(InstallationIdentity.TestDataArea + @"\backups\test.db");
            foreach (var path in new[] { @"D:\ProgramData\ImporterPc\data\Production\station-01\sportmaster\sportmaster.db", @"D:\SharedBackups\original.db" })
                Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireExternalFilePath(path));
        }
        else
        {
            InstallationIdentity.RequireExternalFilePath(@"D:\DedicatedBackups\original.db");
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireExternalFilePath(InstallationIdentity.TestDataArea + @"\backups\test.db"));
        }
    }
    [TestMethod]
    public void MaintenanceCannotBorrowAnotherConfigurationWhenItsRegistrationIsMissing()
    {
        InstallationResourceBoundary.OtherInstallationForVerification = () => null;
        if (InstallationIdentity.IsTest)
        {
            InstallationIdentity.RequireIndependentPath(InstallationIdentity.TestProtectedAnchor + @"\Machine\configuration\sportmaster");
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireIndependentPath(@"D:\ImporterPc\Machine\configuration\sportmaster"));
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireIndependentPath(Path.Combine(root, "unregistered-config")));
        }
        else
        {
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireIndependentPath(InstallationIdentity.TestProtectedAnchor + @"\Machine\configuration\sportmaster"));
        }
    }
    [TestMethod]
    public void LegacyPeerRegistrationAndPreviousDeploymentStillOwnTheirPaths()
    {
        var legacy = peer with { DataRoot = InstallationIdentity.TestDataArea + @"\data" };
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireOutsideOtherInstallation(InstallationIdentity.TestDataArea + @"\data\new", legacy));
        var previous = peer with { ReleaseRoot = Path.Combine(root, "previous"), MaintenanceRoot = Path.Combine(root, "old-cache") };
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireOutsideOtherInstallation(Path.Combine(root, "old-cache", "desktop"), peer with { PreviousDeployment = previous }));
        InstallationIdentity.RequireOutsideOtherInstallation(Path.Combine(root, "independent"), peer);
    }
    [TestMethod]
    public void EffectivePendingAndRetainedSeedReserveCameraAndRcsResources()
    {
        WriteSeed(Config("192.0.2.10", "SEED", "http://192.0.2.20/rcs/sorter/api"));
        var area = Path.Combine(peer.DataRoot, "Production", "station-01", "sportmaster"); Directory.CreateDirectory(area);
        File.WriteAllText(Path.Combine(area, "configuration.json"), new JsonObject
        {
            ["Effective"] = Config("192.0.2.11", "EFFECTIVE", "http://192.0.2.21/rcs/sorter/api"),
            ["Pending"] = Config("192.0.2.12", "PENDING", "http://192.0.2.22/rcs/sorter/api")
        }.ToJsonString());
        foreach (var ip in new[] { "192.0.2.10", "192.0.2.11", "192.0.2.12" })
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera(ip));
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99", "PENDING"));
        foreach (var ip in new[] { "192.0.2.20", "192.0.2.21", "192.0.2.22" })
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireRcsEndpoint(new("http://" + ip + "/rcs/sorter/api")));
        InstallationResourceBoundary.RequireCamera("192.0.2.99", "DEDICATED");
        InstallationResourceBoundary.RequireRcsEndpoint(new("http://192.0.2.99/rcs/sorter/api"));
        // Re-read changed evidence rather than caching an earlier permission.
        WriteSeed(Config("192.0.2.99", "DEDICATED", "http://192.0.2.99/rcs/sorter/api"));
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
    }
    [TestMethod]
    public void MissingOrUnsupportedPeerEvidenceBlocksExternalResources()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireRcsEndpoint(new("http://192.0.2.99/rcs/sorter/api")));
        var config = Config("", "", ""); config["SchemaVersion"] = 2; WriteSeed(config);
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
        File.WriteAllText(Path.Combine(peer.ConfigurationDirectory, "business.json"), "{broken");
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
        config = Config("", "", ""); config["Topology"] = new JsonObject { ["Readers"] = new JsonArray("invalid-reader") }; WriteSeed(config);
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
        InstallationResourceBoundary.OtherInstallationForVerification = () => throw new UnauthorizedAccessException("Synthetic peer registration denial");
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireCamera("192.0.2.99"));
    }
    [TestMethod]
    public void RcsDnsAliasesAndMappedAddressesCannotBypassTheBoundary()
    {
        var ipv4 = IPAddress.Parse("192.0.2.20");
        Assert.ThrowsExactly<InvalidDataException>(() => InstallationResourceBoundary.RequireDistinctRcsAddresses(80, [ipv4], [(80, new[] { IPAddress.Parse("::ffff:192.0.2.20") })]));
        InstallationResourceBoundary.RequireDistinctRcsAddresses(81, [ipv4], [(80, new[] { ipv4 })]);
        InstallationResourceBoundary.RequireDistinctRcsAddresses(80, [IPAddress.Parse("192.0.2.99")], [(80, new[] { ipv4 })]);
    }
    [TestMethod]
    public void CameraOwnershipSharesTheOriginalLeaseAndReleasesPartialAcquisition()
    {
        var formal = Path.Combine(root, "a-formal-leases"); var test = Path.Combine(root, "b-test-leases");
        Directory.CreateDirectory(formal); Directory.CreateDirectory(test);
        using (var oldClient = new ResourceLease(test, ["device:hikrobot-gige:SYNTHETIC"]))
        {
            Assert.ThrowsExactly<IOException>(() => PhysicalCameraOwnership.Acquire("SYNTHETIC", [formal, test]));
            using var rolledBack = new ResourceLease(formal, ["device:hikrobot-gige:SYNTHETIC"]);
        }
        using (var owner = PhysicalCameraOwnership.Acquire("SYNTHETIC", [test, formal]))
        {
            Assert.ThrowsExactly<IOException>(() => new ResourceLease(formal, ["device:hikrobot-gige:SYNTHETIC"]));
            Assert.ThrowsExactly<IOException>(() => new ResourceLease(test, ["device:hikrobot-gige:SYNTHETIC"]));
        }
        using var released = PhysicalCameraOwnership.Acquire("SYNTHETIC", [formal, test]);
    }
    void WriteSeed(JsonObject config) => File.WriteAllText(Path.Combine(peer.ConfigurationDirectory, "business.json"), config.ToJsonString());
    static JsonObject Config(string ip, string serial, string rcs) => new()
    {
        ["SchemaVersion"] = 3, ["ApplicationId"] = "sportmaster",
        ["Reader"] = new JsonObject { ["Parameters"] = new JsonObject { ["device.ip"] = ip, ["device.serial"] = serial } },
        ["Wcs"] = new JsonObject { ["Target"] = rcs }
    };
}
