using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class DriversRegression : CoreRegression
{
    [TestMethod]
    [TestCategory("Core")]
    [TestCategory("Drivers")]
    [DataRow("--camera-acquisition")]
    [DataRow("--camera-acquisition-boundaries")]
    [DataRow("--tilted-decode")]
    [DataRow("--mvid-reader")]
    [DataRow("--camera-chain-reliability")]
    [DataRow("--mvs-network-reconnect")]
    [DataRow("--mvs-offline-activation")]
    [DataRow("--mvs-stop-reconnect")]
    [DataRow("--startup-camera-dependencies")]
    [DataRow("--mvs-presence")]
    [DataRow("--mvs-still-frames")]
    [DataRow("--acquisition-lifecycle")]
    public Task Contracts(string selector) => RunSuite(selector);
}
