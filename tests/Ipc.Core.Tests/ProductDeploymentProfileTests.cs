using Ipc.Operations;
using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass]
public sealed class ProductDeploymentProfileTests
{
    [TestMethod]
    [DataRow("sportmaster", true, true, true)]
    [DataRow("direct-plc", false, true, true)]
    [DataRow("wcs-to-plc", false, true, true)]
    [DataRow("supply", false, false, false)]
    [DataRow("inspection", false, false, false)]
    public void RegisteredProductsDeclareExistingDeploymentCapabilities(string id, bool workstation, bool managed, bool shortcuts)
    {
        var profile = ProductDeploymentCatalog.Require(id);
        Assert.AreEqual(id, profile.ApplicationId);
        Assert.AreEqual(workstation ? InstallationIdentity.DisplayName : "菜鸟供包台 · " + id, profile.DisplayName);
        Assert.AreEqual(shortcuts, profile.SupportsWindowsShortcuts);
        Assert.AreEqual(managed, profile.UsesManagementModeBroker);
        Assert.AreEqual(managed, profile.UsesRecoveryEntry);
        Assert.AreEqual(workstation, profile.UseDualProgramShortcuts);
        Assert.AreEqual(workstation, profile.SupportsAutoStart);
        Assert.AreEqual(workstation, profile.UsesStrictWindowsStorage);
        Assert.AreEqual(workstation, profile.UsesProgramUseLock);
        Assert.AreEqual(workstation, profile.CloseUncommittedOnInstall);
        Assert.AreEqual(workstation, profile.SupportsOpaqueRemoval);
        Assert.AreEqual(workstation, profile.RequiresRestoreHandoff);
        Assert.AreEqual(workstation, profile.RequiresBundledCameraDependencies);
        Assert.AreEqual(workstation, profile.RequiresBundledMvidVcRuntime);
        Assert.AreEqual(workstation, profile.SupportsStandaloneOperationsTool);
        Assert.AreEqual(workstation, profile.UsesQuietOperationJournal);
        Assert.AreEqual(workstation, profile.PreservesInstallationConfiguration);
        Assert.AreEqual(workstation, profile.UsesInlineInstallationReview);
        Assert.AreEqual(workstation, profile.UsesInstallationContinuation);
        Assert.AreEqual(workstation, profile.ShowsAdvancedDeploymentMaintenance);
        Assert.AreEqual(workstation ? "1.0.0" : null, profile.MissingReleaseRepairVersion);
        Assert.AreEqual(workstation, profile.AllowsSameVersionRollback("1.0.0", "1.0.0"));
        Assert.IsFalse(profile.AllowsSameVersionRollback("1.0.0", "1.0.1"));
        Assert.IsFalse(profile.AllowsSameVersionRollback("1.0.1", "1.0.1"));
    }

    [TestMethod]
    public void ProductListsContainOnlyDeclaredIds()
    {
        CollectionAssert.AreEqual(new[] { "sportmaster", "direct-plc", "wcs-to-plc", "supply", "inspection" }, ProductDeploymentCatalog.RegisteredIds.ToArray());
        CollectionAssert.AreEqual(new[] { "sportmaster", "direct-plc", "wcs-to-plc" }, ProductDeploymentCatalog.KnownManagedIds.ToArray());
        CollectionAssert.AreEqual(new[] { "sportmaster" }, ProductDeploymentCatalog.CameraDependencyProducts.ToArray());
        CollectionAssert.AreEqual(new[] { "sportmaster" }, ProductDeploymentCatalog.OperationsEnabledProducts.ToArray());
    }

    [TestMethod]
    [DataRow("unknown-product")]
    [DataRow("Sportmaster")]
    [DataRow("../sportmaster")]
    public void UnknownProductHasNoProfile(string id)
    {
        Assert.IsNull(ProductDeploymentCatalog.Find(id));
        Assert.ThrowsExactly<NotSupportedException>(() => ProductDeploymentCatalog.Require(id));
    }

    [TestMethod]
    [DataRow("unknown-product")]
    [DataRow("supply")]
    [DataRow("inspection")]
    public void UndeclaredShortcutProductCannotPlanOrProjectLinks(string id)
    {
        var location = Location(id);
        Assert.ThrowsExactly<NotSupportedException>(() => DeploymentShortcuts.Plan(location, @"D:\TestMenu", @"D:\TestDesktop", @"D:\TestStartup"));
        Assert.ThrowsExactly<NotSupportedException>(() => DeploymentShortcuts.Create(location, @"D:\TestMenu", @"D:\TestDesktop"));
        Assert.ThrowsExactly<NotSupportedException>(() => DeploymentShortcuts.Remove(location, @"D:\TestMenu", @"D:\TestDesktop"));
        Assert.IsFalse(DeploymentShortcuts.ReadAutoStart(location, @"D:\TestStartup"));
    }

    [TestMethod]
    [DataRow("direct-plc", "设备直连业务")]
    [DataRow("wcs-to-plc", "调度转发业务")]
    public void ClassicShortcutProductsKeepVersionedMenuAndNoAutoStart(string id, string businessName)
    {
        var location = Location(id);
        var plan = DeploymentShortcuts.Plan(location, @"D:\TestMenu", @"D:\TestDesktop", @"D:\TestStartup");
        var prefix = InstallationIdentity.DisplayName + " · " + businessName + " · " + id + " · aaaaaaaa · 12345678";
        CollectionAssert.AreEqual(new[]
        {
            prefix + " · 运行.lnk", prefix + " · 启动修复.lnk", prefix + " · 安装维护.lnk", InstallationIdentity.DisplayName + ".lnk"
        }, plan.Select(link => Path.GetFileName(link.Path)).ToArray());
        CollectionAssert.AreEqual(new[] { "", "--maintenance", "--setup", "" }, plan.Select(link => link.Arguments).ToArray());
        Assert.IsTrue(plan.All(link => link.Target == Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable)));
    }

    [TestMethod]
    [DataRow("unknown-product")]
    [DataRow("direct-plc")]
    [DataRow("wcs-to-plc")]
    [DataRow("supply")]
    [DataRow("inspection")]
    public void UnentitledProductCannotOpenStandaloneOperations(string id)
    {
        // Reject the supplied product identity before opening a deployment or inventory path.
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => OperationsInstallation.Load("", id));
    }

    static DeploymentLocation Location(string id) => new(id, new string('a', 40), @"D:\Releases\build-12345678",
        @"D:\Config", @"D:\Data", "S-1-5-21-100-200-300-1001", Shortcuts: new(true, true, true));
}
