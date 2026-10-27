using System.Reflection;
using System.Runtime.ExceptionServices;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static void VerifyPreservedSeedUpgrade(string desktopDirectory, string evidence, Action<bool, string> check)
    {
        var service = Assembly.LoadFrom(Path.Combine(desktopDirectory, "Ipc.Assembly.dll")).GetType("Ipc.Assembly.ParcelStartupServices", true)!;
        var validate = service.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "ValidateSeed" && m.GetParameters().Length == 2);
        void Validate(WindowsDeploymentRequest r, DeploymentLocation? d)
        {
            try { validate.Invoke(null, [r, d]); }
            catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        var root = Path.Combine(evidence, "original-seed-guards");
        var config = Path.Combine(root, "configuration"); Directory.CreateDirectory(config);
        var seed = new SportmasterFlow().InitialConfiguration();
        seed = seed with { DeferredConnections = null }; // Current incomplete seed, never an old component fixture.
        var file = Path.Combine(config, "business.json"); File.WriteAllText(file, Wire.Encode(seed));
        var hash = Wire.FileHash(file);
        var installed = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "old-release"), config, Path.Combine(root, "data"), MachineRuntimePolicy.SharedUsersSid);
        var request = new WindowsDeploymentRequest(root, "sportmaster", new string('b', 40), installed.RuntimeAccountSid, installed.DataRoot, config,
            RunMode.Production, null, null, [], ConfigurationHashes: new() { ["business.json"] = hash }, ExpectedRegistrationHash: new string('C', 64));
        check(new SportmasterFlow().Validate(seed).Length > 0, "current incomplete seed is not approved for runtime");
        Validate(request, installed);
        check(Wire.FileHash(file) == hash && !Directory.Exists(installed.DataRoot), "same original unconfigured seed passes installation validation without writes or runtime admission");
        void Reject(WindowsDeploymentRequest r, DeploymentLocation? d, string message)
        {
            var denied = false; try { Validate(r, d); } catch (InvalidDataException) { denied = true; }
            check(denied, message);
        }
        Reject(request, null, "incomplete non-template draft cannot pose as a new exact initial template");
        Reject(request with { RepairSeed = true }, installed, "replacing an invalid seed cannot use original-retention exemption");
        Reject(request with { DataRoot = Path.Combine(root, "redirected") }, installed, "invalid retained seed cannot redirect the original data root");
        Reject(request with { RuntimeAccountSid = "S-1-5-21-1-2-3-1001" }, installed, "invalid retained seed cannot change its runtime account");
        Reject(request with { ConfigurationHashes = new() { ["business.json"] = new string('D', 64) } }, installed, "changed reviewed seed bytes cannot use retention exemption");
        Reject(request with { ExpectedRegistrationHash = null }, installed, "unfixed registration cannot use retention exemption");
        Reject(request, installed with { LifecycleState = "RemovedDataRetained" }, "retired installation cannot use the Installed exemption");
        var elsewhere = Path.Combine(root, "replacement"); Directory.CreateDirectory(elsewhere); File.Copy(file, Path.Combine(elsewhere, "business.json"));
        Reject(request with { ConfigurationSource = elsewhere }, installed, "even identical replacement-directory bytes require full new-seed validation");
        foreach (var schema in new[] { 1, 2, 999 })
        {
            File.WriteAllText(file, Wire.Encode(seed with { SchemaVersion = schema }));
            var rejectedHash = Wire.FileHash(file);
            Reject(request with { ConfigurationHashes = new() { ["business.json"] = rejectedHash } }, installed, "noncurrent schema refuses original-retention exemption");
            check(Wire.FileHash(file) == rejectedHash, "rejected seed bytes remain unchanged");
        }
        File.WriteAllText(file, Wire.Encode(seed));
        check(Wire.FileHash(file) == hash, "all negative controls preserve the original synthetic seed identity");
    }
}
