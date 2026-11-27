using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class CapabilityRegistryTests : CoreRegression
{
    [TestMethod]
    [TestCategory("Core")]
    [DataRow("--capability-registry-routing")]
    [DataRow("--capability-registry-discovery")]
    [DataRow("--capability-registry-live")]
    [DataRow("--rcs-component-binding")]
    [DataRow("--rcs-component-dispatch")]
    public Task Contracts(string selector) => RunSuite(selector);
}
