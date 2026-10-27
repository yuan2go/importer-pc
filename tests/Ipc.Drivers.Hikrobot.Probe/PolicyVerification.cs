using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ipc.Drivers.Hikrobot;
using Ipc.Platform;

internal static class PolicyVerification
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipc-native-policy-" + Wire.Id()); Directory.CreateDirectory(root);
        using var signer = RSA.Create(3072);
        var trust = new ExtensionTrust(1, [new("native-policy-test", signer.ExportSubjectPublicKeyInfoPem(), true)]);
        var dll = File.ReadAllBytes(typeof(HikrobotModule).Assembly.Location);
        var file = new ExtensionFile("Ipc.Drivers.Hikrobot.dll", dll.Length, Convert.ToHexString(SHA256.HashData(dll)));
        var declaration = new ExtensionPrerequisite("vendor-sdk", "MvCodeReaderCtrl.dll", "2.0.0.4", new('A', 64));
        var module = new ExtensionModule("idmvs-reader", file.Path, "Ipc.Drivers.Hikrobot.HikrobotModule", "observation", 8,
            [new("device.ip", "device", "Camera IPv4", "string", true)]);
        var manifest = new ExtensionManifest(1, "test.native-reader", "1.0.0", "native-policy-test", "net10.0", "win-x64", 1,
            RunMode.Simulation, new('a', 40), [], [file], [module], [declaration], Wire.FileHash(typeof(ExtensionStore).Assembly.Location));
        string Package(ExtensionManifest value, ExtensionFile? artifact = null, byte[]? payload = null)
        {
            var archive = Path.Combine(root, Wire.Id() + ".ipcx");
            var bytes = Encoding.UTF8.GetBytes(Wire.Encode(value));
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            void Entry(string name, byte[] content) { using var output = zip.CreateEntry(name).Open(); output.Write(content); }
            Entry(artifact?.Path ?? file.Path, payload ?? dll); Entry("manifest.json", bytes);
            Entry("manifest.sig", signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
            return archive;
        }
        var accepted = Package(manifest);
        var verified = ExtensionPackages.Inspect(accepted, trust, RunMode.Simulation);
        Console.WriteLine("PASS: signed declared vendor import accepted statically without loading SDK");
        void Reject(ExtensionManifest invalid)
        {
            try { ExtensionPackages.Inspect(Package(invalid), trust, RunMode.Simulation); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Undeclared/ambiguous native boundary accepted");
        }
        Reject(manifest with { Prerequisites = [] });
        Reject(manifest with { Prerequisites = [declaration with { Identity = "kernel32.dll" }] });
        Reject(manifest with { Prerequisites = [declaration, declaration] });
        Reject(manifest with { Rid = "any" });
        Reject(manifest with { Prerequisites = [declaration with { Preload = ["../foreign.dll"] }] });
        Reject(manifest with { Prerequisites = [declaration with { Preload = ["MvCodeReaderCtrl.dll"] }] });
        Console.WriteLine("PASS: missing/wrong/duplicate native prerequisite and portable native package rejected");
        var store = new ExtensionStore(Path.Combine(root, "store"), trust, RunMode.Simulation, Path.Combine(root, "leases"));
        Directory.CreateDirectory(store.Root);
        var destination = store.PackageDirectory(verified.Reference); Directory.CreateDirectory(destination);
        ExtensionPackages.ExtractVerified(accepted, destination, trust, RunMode.Simulation, verified.PackageSha256);
        // Isolated test inventory; never invokes/provisions the machine installer.
        ExtensionStore.AtomicWrite(Path.Combine(store.Root, "registrations", "test.json"), new ExtensionInstallation(
            verified.Reference, verified.PackageSha256, verified.TrustSha256, Wire.Id(), DateTimeOffset.UtcNow, "Installed", "Synthetic policy fixture"));
        using (var loading = new ExtensionLoading(store))
        {
            await using var reader = loading.Create(verified.Reference, module,
                new("camera", verified.Reference.Id, module.Id, "generic-reader", true, new() { ["device.ip"] = "192.0.2.1" }));
            bool rejected = false;
            try { await reader.StartControlledAsync("no-qualification", _ => true, _ => { }, CancellationToken.None); }
            catch (InvalidDataException ex) when (ex.Message.Contains("qualification", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Native reader started without independent qualification");
        }
        Console.WriteLine("PASS: actual ALC reader startup refuses native SDK without independent qualification");
        var load = typeof(NativeSdkBoundary).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
        var proof = new PrerequisiteQualification(verified.Reference, declaration, [new(Path.Combine(root, "not-installed.dll"), new('B', 64))]);
        var qualification = new DeploymentQualification(1, RunMode.Simulation, new('C', 64), manifest.HostContractSha256, new('D', 64),
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), [], [proof]);
        var withPreload = declaration with { Preload = ["MVGigEVision.dll", "MvSmtDevCtrl.dll"] };
        var roundtrip = Wire.Decode<DeploymentQualification>(Wire.Encode(qualification with
            { Prerequisites = [proof with { Declaration = withPreload }] }));
        DeploymentQualifications.RequirePrerequisites(roundtrip, verified with { Manifest = manifest with { Prerequisites = [withPreload] } });
        Console.WriteLine("PASS: signed preload declaration retains value equality across independent JSON decoding");
        foreach (var invalid in new[] { qualification with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }, qualification with { Prerequisites = [] }, qualification })
        {
            try { load.Invoke(null, [manifest, verified.Reference, invalid, declaration.Identity]); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { continue; }
            throw new InvalidOperationException("Expired/mismatched/missing-file native proof accepted");
        }
        Console.WriteLine("PASS: expired qualification, wrong prerequisite owner and missing native entry bytes rejected");
        var mvsBytes = File.ReadAllBytes(typeof(Ipc.Drivers.Hikrobot.Mvs.MvsAcquisitionModule).Assembly.Location);
        var mvsFile = new ExtensionFile("Ipc.Drivers.Hikrobot.Mvs.dll", mvsBytes.Length, Convert.ToHexString(SHA256.HashData(mvsBytes)));
        var mvsDeclaration = new ExtensionPrerequisite("vendor-sdk", "MvCameraControl.dll", "4.8.2.2", new('B', 64)) { Preload = ["MVGigEVisionSDK.dll"] };
        var mvsManifest = manifest with { Id = "test.mvs-acquisition", Files = [mvsFile], Prerequisites = [mvsDeclaration],
            Modules = [new("mvs-acquisition", mvsFile.Path, "Ipc.Drivers.Hikrobot.Mvs.MvsAcquisitionModule", "observation", 2, [new("device.ip", "device", "Camera IPv4", "string", true)])] };
        var mvsArchive = Package(mvsManifest, mvsFile, mvsBytes);
        var mvsPackage = ExtensionPackages.Inspect(mvsArchive, trust, RunMode.Simulation);
        bool missingMvsRejected = false;
        try { ExtensionPackages.Inspect(Package(mvsManifest with { Prerequisites = [] }, mvsFile, mvsBytes), trust, RunMode.Simulation); }
        catch (InvalidDataException) { missingMvsRejected = true; }
        if (!missingMvsRejected) throw new InvalidOperationException("MVS native imports accepted without declaration");
        var mvsDestination = store.PackageDirectory(mvsPackage.Reference); Directory.CreateDirectory(mvsDestination);
        ExtensionPackages.ExtractVerified(mvsArchive, mvsDestination, trust, RunMode.Simulation, mvsPackage.PackageSha256);
        ExtensionStore.AtomicWrite(Path.Combine(store.Root, "registrations", "mvs-test.json"), new ExtensionInstallation(
            mvsPackage.Reference, mvsPackage.PackageSha256, mvsPackage.TrustSha256, Wire.Id(), DateTimeOffset.UtcNow, "Installed", "Synthetic MVS policy fixture"));
        using (var loadingMvs = new ExtensionLoading(store))
        {
            await using var mvsSource = loadingMvs.Create(mvsPackage.Reference, mvsPackage.Manifest.Modules.Single(), new("mvs", mvsPackage.Reference.Id, "mvs-acquisition", "mvs-reader", true, new() { ["device.ip"] = "192.0.2.1" }));
            bool refused = false;
            try { await mvsSource.StartControlledAsync("mvs-missing-proof", _ => true, _ => { }, default); }
            catch (InvalidDataException ex) when (ex.Message.Contains("qualification", StringComparison.OrdinalIgnoreCase)) { refused = true; }
            if (!refused) throw new InvalidOperationException("MVS SDK loaded without independent qualification");
        }
        Console.WriteLine("PASS: MVS signed import boundary and real ALC missing-qualification refusal preserve current native policy");
        Console.WriteLine("NATIVE_POLICY_PASS");
    }
}
