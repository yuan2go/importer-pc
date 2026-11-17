using System.Security.Principal;

namespace Ipc.Operations;

// UI-thread owned, per installation/account/session. Never takes the business lease.
public sealed class OperationsToolLease : IDisposable
{
    readonly Mutex mutex;
    public OperationsToolLease(OperationsInstallation installation)
    {
        installation.RequireRead();
        using var identity = WindowsIdentity.GetCurrent();
        mutex = new Mutex(false, @"Local\ImporterPc.Operations." + installation.Identity[..32] + "." + identity.User!.Value);
        bool entered;
        try { entered = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { entered = true; }
        if (!entered) { mutex.Dispose(); throw new InvalidOperationException("供包运维工具已打开，请切换到现有窗口。"); }
    }
    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}
