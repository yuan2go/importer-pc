using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ipc.Platform;

public static partial class ManagementModeBroker
{
    internal static readonly (uint Type, uint Delay)[] RecoveryActions = [(1, 5000), (1, 30000), (0, 0)];
    internal const uint RecoveryResetSeconds = 86400;
    internal static string RecoveryServiceIdentity(string dataRoot) => ServiceName(dataRoot);
    sealed class RecoverySnapshot : IDisposable
    {
        internal readonly IntPtr Actions, NonCrash;
        internal RecoverySnapshot(ServiceHandle service)
        {
            Actions = ReadServiceConfiguration(service, 2);
            try { NonCrash = ReadServiceConfiguration(service, 4); }
            catch { Marshal.FreeHGlobal(Actions); throw; }
        }
        internal void Restore(ServiceHandle service)
        {
            if (!ChangeServiceConfig2(service, 2, Actions) || !ChangeServiceConfig2(service, 4, NonCrash))
                throw NativeFailure("ChangeServiceConfig2W(restore)");
        }
        public void Dispose() { Marshal.FreeHGlobal(Actions); Marshal.FreeHGlobal(NonCrash); }
    }
    static IntPtr ReadServiceConfiguration(ServiceHandle service, uint level)
    {
        if (!QueryServiceConfig2(service, level, IntPtr.Zero, 0, out var required) && Marshal.GetLastWin32Error() != 122)
            throw NativeFailure("QueryServiceConfig2W(size)");
        if (required is < 4 or > 65536) throw new InvalidDataException("原服务恢复设置超出核对预算。");
        var buffer = Marshal.AllocHGlobal((int)required);
        if (QueryServiceConfig2(service, level, buffer, required, out _)) return buffer;
        var code = Marshal.GetLastWin32Error(); Marshal.FreeHGlobal(buffer); throw NativeFailure("QueryServiceConfig2W", code);
    }
    [SupportedOSPlatform("windows")]
    static void ConfigureRecovery(ServiceHandle service)
    {
        var actions = Marshal.AllocHGlobal(RecoveryActions.Length * Marshal.SizeOf<RecoveryAction>());
        var settings = Marshal.AllocHGlobal(Marshal.SizeOf<RecoverySettings>());
        var nonCrash = Marshal.AllocHGlobal(4);
        try
        {
            for (var i = 0; i < RecoveryActions.Length; i++) Marshal.StructureToPtr(new RecoveryAction { Type = RecoveryActions[i].Type, Delay = RecoveryActions[i].Delay },
                IntPtr.Add(actions, i * Marshal.SizeOf<RecoveryAction>()), false);
            Marshal.StructureToPtr(new RecoverySettings { ResetPeriod = RecoveryResetSeconds, Count = (uint)RecoveryActions.Length, Actions = actions }, settings, false);
            if (!ChangeServiceConfig2(service, 2, settings)) throw NativeFailure("ChangeServiceConfig2W(SERVICE_CONFIG_FAILURE_ACTIONS=2)");
            Marshal.WriteInt32(nonCrash, 1);
            if (!ChangeServiceConfig2(service, 4, nonCrash)) throw NativeFailure("ChangeServiceConfig2W(SERVICE_CONFIG_FAILURE_ACTIONS_FLAG=4)");
        }
        finally { Marshal.FreeHGlobal(nonCrash); Marshal.FreeHGlobal(settings); Marshal.FreeHGlobal(actions); }
    }
    [StructLayout(LayoutKind.Sequential)] struct RecoveryAction { public uint Type, Delay; }
    [StructLayout(LayoutKind.Sequential)] struct RecoverySettings { public uint ResetPeriod; public IntPtr RebootMessage, Command; public uint Count; public IntPtr Actions; }
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    static extern bool ChangeServiceConfig2(ServiceHandle service, uint level, IntPtr value);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
    static extern bool QueryServiceConfig2(ServiceHandle service, uint level, IntPtr value, uint size, out uint required);
}
