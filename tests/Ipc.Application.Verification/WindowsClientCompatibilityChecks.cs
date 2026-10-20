using System.Runtime.InteropServices;
using Ipc.Platform;

static class WindowsClientCompatibilityChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; Console.WriteLine("PASS " + reason); }
        foreach (var build in new[] { 19045, 22000, 22631, 26100, 26200 })
        {
            var result = WindowsClientCompatibility.Assess(new(10, 0, build), Architecture.X64, Architecture.X64);
            Check(result.CanStart && result.PrimaryTarget, "Win10 22H2/Win11 x64 functional target classification: " + build);
        }
        foreach (var build in new[] { 14393, 17763, 19044 })
        {
            var result = WindowsClientCompatibility.Assess(new(10, 0, build), Architecture.X64, Architecture.X64);
            Check(result.CanStart && !result.PrimaryTarget, "older Win10 baseline stays explicit and separately unqualified: " + build);
        }
        Check(!WindowsClientCompatibility.Assess(new(6, 3, 9600), Architecture.X64, Architecture.X64).CanStart, "Windows8 is outside runtime floor");
        Check(!WindowsClientCompatibility.Assess(new(10, 0, 10240), Architecture.X64, Architecture.X64).CanStart, "original Windows10 build is outside runtime floor");
        Check(!WindowsClientCompatibility.Assess(new(10, 0, 19045), Architecture.X86, Architecture.X86).CanStart, "32bit Windows is not called x64 compatible");
        Check(!WindowsClientCompatibility.Assess(new(10, 0, 22631), Architecture.Arm64, Architecture.X64).CanStart, "ARM64 emulation is separately unqualified");
        Console.WriteLine($"PASS {checks} pure compatibility policy checks; synthetic OS tuples, not actual Win10 or alternative CPU execution");
    }
}
