using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass, DoNotParallelize]
public sealed class DeploymentRegression : CoreRegression
{
    [TestMethod]
    [TestCategory("Core")]
    [TestCategory("Deployment")]
    [DataRow("--installation-resource-boundary")]
    [DataRow("--industrial-lifecycle")]
    [DataRow("--industrial-rollback")]
    [DataRow("--industrial-version-pruning")]
    [DataRow("--permanent-data-deletion")]
    [DataRow("--uninstall-data-boundary")]
    [DataRow("--uninstall-assets")]
    [DataRow("--industrial-opaque-removal")]
    [DataRow("--industrial-quiet-deployment")]
    [DataRow("--shortcut-lifecycle")]
    [DataRow("--win11-stability")]
    [DataRow("--installation-receipts")]
    [DataRow("--installation-configuration")]
    [DataRow("--installer-storage-checks")]
    public Task Contracts(string selector) => RunSuite(selector);
}
