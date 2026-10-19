using Ipc.Platform;
using System.Security.Claims;

static class RuntimeIdentityVerification
{
    public static void Run()
    {
        const string designated = "S-1-5-21-100-200-300-1001";
        MachineRuntimePolicy.ValidateRuntimeAccount(designated, designated, true, []);
        Console.WriteLine("PASS identity: designated ordinary SID accepted by shared decision");
        Denied("S-1-5-21-100-200-300-1002", designated, [], "other ordinary SID");
        foreach (var type in new[] { ClaimTypes.GroupSid, ClaimTypes.DenyOnlySid, ClaimTypes.PrimaryGroupSid, ClaimTypes.DenyOnlyPrimaryGroupSid })
            Denied(designated, designated, [new(type, "S-1-5-32-544")], "designated administrator membership: " + type);
        foreach (var service in new[] { "S-1-5-18", "S-1-5-19", "S-1-5-20" })
            Denied(service, service, [], "designated system/service SID");
        MachineRuntimePolicy.ValidateRuntimeAccount(designated, designated, false, [new(ClaimTypes.GroupSid, "S-1-5-32-544")]);
        Console.WriteLine("PASS identity: legacy lease resolution retains separate maintenance semantics");
        foreach (var invalid in new string?[] { null, "", " " })
        {
            Denied(invalid, designated, [], "missing current SID");
            Denied(designated, invalid, [], "missing designated SID");
        }
        if (!OperatingSystem.IsWindows())
        {
            MachineRuntimePolicy.RequireRuntimeIdentity();
            Console.WriteLine("PASS identity: explicit portable development gate preserved; Windows identity/ACL NOT_RUN");
        }
    }
    static void Denied(string? current, string? designated, Claim[] claims, string scenario)
    {
        try { MachineRuntimePolicy.ValidateRuntimeAccount(current, designated, true, claims); }
        catch (UnauthorizedAccessException) { Console.WriteLine("PASS identity: rejected " + scenario); return; }
        throw new Exception("FAIL identity: accepted " + scenario);
    }
}
