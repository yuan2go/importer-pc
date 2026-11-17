using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class UiArgumentsRegression
{
    [TestMethod]
    public void RootPositionDoesNotChangeScenario()
    {
        CollectionAssert.AreEqual(UiVerificationArguments.Normalize([".", "--camera-debug"]), UiVerificationArguments.Normalize(["--camera-debug", "."]));
        CollectionAssert.AreEqual(new[] { Path.GetFullPath("."), "--typography-comparison", "--observe-only", "--system-family" }, UiVerificationArguments.Normalize(["--typography-comparison", ".", "--observe-only", "--system-family"]));
        Assert.AreEqual(6, UiVerificationArguments.Normalize([".", "--latency-capacity", "--latency-assert", "--latency-rows", "25", "--baseline"]).Length);
    }
    [TestMethod]
    [DataRow("--unknown")]
    [DataRow("--camera-debug --camera-round-ui")]
    [DataRow("--camera-debug --camera-debug")]
    [DataRow("--camera-debug --observe-only")]
    [DataRow("--camera-debug --latency-assert")]
    [DataRow("--latency-capacity --latency-rows")]
    [DataRow("--typography-comparison --system-family --system-family")]
    [DataRow("--p0-crash-hooks --unknown")]
    public void InvalidSelectorsCannotFallThrough(string arguments)
    {
        Assert.ThrowsExactly<ArgumentException>(() => UiVerificationArguments.Normalize(arguments.Split(' ')));
    }
}
