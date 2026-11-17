using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class SmRemovalRegression
{
    [TestMethod]
    public void SharedSmRemovalKernelUsesAnIsolatedBoundedFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "cr-sm-removal-" + Guid.NewGuid().ToString("N"));
        CommercialRemovalVerification.Run(root);
        Assert.IsFalse(Directory.Exists(root), "The complete isolated fixture must be cleaned after verification.");
    }
}
