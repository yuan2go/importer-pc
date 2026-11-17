using System.Reflection;
using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ScopedManagementExit
{
    [TestMethod]
    public void ClosingOldWindowPreservesReplacementAuthorization()
    {
        var mode = new LocalManagementMode(Path.Combine(Path.GetTempPath(), "ipc-settings-grant-" + Wire.Id()));
        mode.ConfigurePassword("SYNTHETIC-12345"); Assert.IsTrue(mode.Enter("SYNTHETIC-12345"));
        var old = mode.Status().SessionId!;
        Assert.IsTrue(mode.Enter("SYNTHETIC-12345")); var current = mode.Status().SessionId!;
        mode.Exit(old);
        Assert.IsTrue(mode.Status().Authorized); Assert.AreEqual(current, mode.Status().SessionId);
        mode.Exit(current); Assert.IsFalse(mode.Status().Authorized);
    }
    [TestMethod]
    public void FailedRemoteExitRetainsOriginalTokenForActualRetry()
    {
        var mode = new LocalManagementMode(Path.Combine(Path.GetTempPath(), "ipc-settings-offline-" + Wire.Id()));
        mode.ConfigurePassword("SYNTHETIC-12345"); Assert.IsTrue(mode.Enter("SYNTHETIC-12345"));
        var original = mode.Status().SessionId!;
        var token = typeof(LocalManagementMode).GetField("sessionSecret", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remote = typeof(LocalManagementMode).GetField("remoteObserved", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var expected = (string)token.GetValue(mode)!;
        // Boundary fault: this unique root has no SCM/pipe service. Do not create
        // or replace the machine service merely to simulate a failed connection.
        remote.SetValue(mode, true);
        try
        {
            Assert.ThrowsExactly<IOException>(() => mode.Exit(original));
            Assert.AreEqual(expected, token.GetValue(mode), "A lost revocation reply must retain the original proof, not a new grant.");
        }
        finally { remote.SetValue(mode, false); mode.Exit(original); }
        Assert.IsFalse(mode.Status().Authorized);
    }
}
