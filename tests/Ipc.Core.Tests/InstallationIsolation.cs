using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass]
public sealed class InstallationIsolation
{
    [TestMethod]
    public void InstallationNamespacesPreserveDefaultAndSeparateTestAuthority()
    {
        var test = InstallationIdentity.IsTest;
        Assert.AreEqual(test ? @"D:\ImporterPcTest" : @"D:\ImporterPc", WindowsStoragePaths.DefaultProtectedAnchor);
        Assert.AreEqual(test ? @"SOFTWARE\ImporterPc.Test\Storage" : @"SOFTWARE\ImporterPc\Storage", InstallationIdentity.StorageRegistryKey);
        Assert.AreEqual(test ? "ImporterPc.Test." : "ImporterPc.", InstallationIdentity.UninstallKeyPrefix.Split('\\').Last());
        Assert.AreEqual(test ? @"D:\CainiaoSupplyStationTest" : @"D:\CainiaoSupplyStation", InstallationIdentity.DefaultProgramRoot);
        Assert.AreEqual(test ? @"D:\ProgramData\ImporterPcTest\data" : @"D:\ProgramData\ImporterPc\data", InstallationIdentity.DefaultDataRoot);
    }

    [TestMethod]
    public void TestInstanceRejectsProductionAndLookalikePaths()
    {
        InstallationIdentity.RequireDataRoot(InstallationIdentity.DefaultDataRoot);
        InstallationIdentity.RequireProgramRoot(InstallationIdentity.DefaultProgramRoot);
        if (InstallationIdentity.IsTest)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireDataRoot(@"D:\ProgramData\ImporterPc\data"));
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireDataRoot(@"D:\ProgramData\ImporterPcTest\data-other"));
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireDataRoot(@"D:\ProgramData\ImporterPcTest\data\..\..\ImporterPc\data"));
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireProgramRoot(@"D:\CainiaoSupplyStation"));
            Assert.ThrowsExactly<InvalidDataException>(() => ApplicationRootOwnership.Resource("sportmaster", @"D:\ProgramData\ImporterPc\data"));
        }
        else
        {
            InstallationIdentity.RequireDataRoot(@"D:\ProgramData\ImporterPc\data");
            InstallationIdentity.RequireProgramRoot(@"D:\CainiaoSupplyStation");
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireDataRoot(@"D:\ProgramData\ImporterPcTest\data"));
            Assert.ThrowsExactly<InvalidDataException>(() => InstallationIdentity.RequireProgramRoot(@"D:\CainiaoSupplyStationTest"));
        }
    }

    [TestMethod]
    public void ShortcutsAndCrashRecoveryUseTheSelectedInstallation()
    {
        var location = new DeploymentLocation("sportmaster", new string('a', 40),
            Path.Combine(InstallationIdentity.DefaultProgramRoot, "releases", "sportmaster-1234567890"),
            Path.Combine(InstallationIdentity.DefaultDataRoot, "configuration"),
            InstallationIdentity.DefaultDataRoot, "S-1-5-21-100-200-300-1001",
            Shortcuts: new(true, true)) { Recovery = new(true) };
        var links = DeploymentShortcuts.Plan(location, @"D:\TestMenu", @"D:\TestDesktop");
        Assert.AreEqual(3, links.Length);
        Assert.AreEqual(InstallationIdentity.IsTest ? "菜鸟供包台（测试）.lnk" : "菜鸟供包台.lnk", Path.GetFileName(links.Last().Path));
        Assert.AreEqual(3, links.Count(link => Path.GetFileName(link.Target) == InstallationIdentity.DesktopExecutable));
        Assert.AreEqual(1, links.Count(link => link.Arguments == InstallationIdentity.OperationsArgument));
        Assert.AreEqual(2, links.Count(link => link.Arguments == ""));
        Assert.AreEqual(2, DeploymentShortcuts.Plan(location with { Shortcuts = new(true, false) }, @"D:\TestMenu", @"D:\TestDesktop").Length);
        Assert.AreEqual(0, DeploymentShortcuts.Plan(location with { Shortcuts = new(false, false) }, @"D:\TestMenu", @"D:\TestDesktop").Length);
        var recovery = DesktopRecoveryInstallation.Plan(location);
        StringAssert.StartsWith(recovery.TaskName, InstallationIdentity.IsTest ? "ImporterPc.Test-sportmaster-" : "ImporterPc-sportmaster-");
        StringAssert.EndsWith(recovery.DumpRegistryKey, InstallationIdentity.IsTest ? @"\Ipc.Desktop.Test.exe" : @"\Ipc.Desktop.exe");
    }
}
