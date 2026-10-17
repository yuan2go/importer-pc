using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.Logging.Abstractions;

// Independent audit reproduction. No production endpoint, device, original verifier,
// repository source or existing data is modified. The supplied plugin is the unmodified
// repository example, built only against the current host's public contract assemblies.
if (args.Length != 3) throw new ArgumentException("usage: HealthRepro <new evidence directory> <independent integration DLL> <source SHA>");
var evidence = Path.GetFullPath(args[0]);
if (Directory.Exists(evidence)) throw new IOException("Use a fresh evidence directory");
Directory.CreateDirectory(evidence);
var plugin = Path.GetFullPath(args[1]);
var sourceSha = args[2];
SupplyContractRegistration.Register();

using var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // Bound, never listening: controlled refusal.
var auxiliaryUrl = $"http://127.0.0.1:{((IPEndPoint)reservation.LocalEndPoint!).Port}/";
var finder = new TcpListener(IPAddress.Loopback, 0);
finder.Start();
var primaryPort = ((IPEndPoint)finder.LocalEndpoint).Port;
finder.Stop();
var primaryUrl = $"http://127.0.0.1:{primaryPort}/";
using var receiver = new HttpListener();
receiver.Prefixes.Add(primaryUrl);
receiver.Start();
int healthCalls = 0, effectCalls = 0;
var server = Task.Run(async () =>
{
    while (receiver.IsListening)
    {
        HttpListenerContext context;
        try { context = await receiver.GetContextAsync(); }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
        var health = context.Request.Url!.AbsolutePath == "/health";
        if (health) Interlocked.Increment(ref healthCalls); else Interlocked.Increment(ref effectCalls);
        context.Response.StatusCode = health ? 200 : 405;
        var bytes = Encoding.UTF8.GetBytes(health ? "SIMULATION-v1" : "Audit receiver permits health only");
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
});

using var signingKey = RSA.Create(3072);
var trust = new ExtensionTrust(1, [new("audit-local", signingKey.ExportSubjectPublicKeyInfoPem(), true)]);
var trustFile = Path.Combine(evidence, "trust.json");
File.WriteAllText(trustFile, Wire.Encode(trust));
var contractRoot = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
var packageManifest = new ExtensionManifest(1, "audit.integration", "1.0.0", "audit-local", "net10.0", "any", 1, RunMode.Simulation,
    sourceSha, [], [new(Path.GetFileName(plugin), new FileInfo(plugin).Length, Wire.FileHash(plugin))],
    [new("reference-http", Path.GetFileName(plugin), "Ipc.Reference.IntegrationExtension.IntegrationModule", "integration", 8,
        [new("metadata.label", "Service", "Label", "string", Default: "")])], [], Wire.FileHash(typeof(IObservationExtension).Assembly.Location),
    new() { ["Ipc.Supply.Contracts"] = Wire.FileHash(typeof(IIntegrationExtension).Assembly.Location) });
var archive = Path.Combine(evidence, "audit.integration.ipcx");
using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
{
    void Add(string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
    var metadata = Encoding.UTF8.GetBytes(Wire.Encode(packageManifest));
    Add("manifest.json", metadata);
    Add("manifest.sig", signingKey.SignData(metadata, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    Add(Path.GetFileName(plugin), File.ReadAllBytes(plugin));
}
var leases = Path.Combine(evidence, "leases");
var extensions = new ExtensionStore(Path.Combine(evidence, "extension-store"), trust, RunMode.Simulation, leases);
var installation = extensions.Install(archive, Wire.FileHash(archive));

async Task<CaseResult> RunCase(bool auxiliaryEnabled)
{
    var label = auxiliaryEnabled ? "auxiliary-enabled-offline" : "primary-only-control";
    var primary = new IntegrationBinding("primary-target", "1", "SIMULATION-v1", primaryUrl);
    var auxiliary = new IntegrationBinding("auxiliary-target", "1", "SIMULATION-v1", auxiliaryUrl);
    var configuration = new StationConfig(1, "SIM-AUDIT-HEALTH", "audit-health-1", RunMode.Simulation,
        [new("left", "manual", "SIM-AUDIT-LEFT", "1"), new("right", "manual", "SIM-AUDIT-RIGHT", "1")], primary,
        MinimumFreeMb: 1, Extensions: new([installation.Package], [],
            [new("primary", installation.Package.Id, "reference-http", true, new() { ["metadata.label"] = "primary" }),
             new("auxiliary", installation.Package.Id, "reference-http", auxiliaryEnabled, new() { ["metadata.label"] = "auxiliary" })],
            auxiliaryEnabled ? [new("requests", "primary", primary), new("auxiliary-status", "auxiliary", auxiliary)] : [new("requests", "primary", primary)]));
    var definition = ReferenceProduct.Definition;
    var manifest = new ProductManifest(1, definition.ProductId, configuration.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
        definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(contractRoot, x.File)))).ToArray());
    var caseRoot = Path.Combine(evidence, label);
    Directory.CreateDirectory(caseRoot);
    var configFile = Path.Combine(caseRoot, "station.json");
    var manifestFile = Path.Combine(caseRoot, "product.json");
    var presentationFile = Path.Combine(caseRoot, "presentation.json");
    File.WriteAllText(configFile, Wire.Encode(configuration));
    File.WriteAllText(manifestFile, Wire.Encode(manifest));
    File.WriteAllText(presentationFile, Wire.Encode(new PresentationSettings()));
    var callsBefore = Volatile.Read(ref healthCalls);
    await using var runtime = new ProductRuntime(new(manifestFile, configFile, presentationFile, Path.Combine(caseRoot, "data"), contractRoot, leases,
        ExtensionRoot: extensions.Root, ExtensionTrustFile: trustFile), NullLogger<ProductRuntime>.Instance);
    await runtime.StartAsync(CancellationToken.None);
    if (runtime.Diagnostics.Count != 0 || runtime.Application is null) throw new InvalidOperationException(string.Join("; ", runtime.Diagnostics));
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (Volatile.Read(ref healthCalls) < callsBefore + 2)
    {
        if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("No current integration health attempts");
        await Task.Delay(25);
    }
    await Task.Delay(100);
    var snapshot = await runtime.Application.SnapshotAsync();
    var result = new CaseResult(label, Volatile.Read(ref healthCalls) - callsBefore, snapshot.State.IntegrationServiceHealth,
        runtime.IntegrationDiagnostics, snapshot.Admit.Reasons, runtime.Diagnostics.ToArray());
    File.WriteAllText(Path.Combine(caseRoot, "result.json"), Wire.Encode(result));
    return result;
}

try
{
    var failed = await RunCase(true);
    var control = await RunCase(false);
    var reproduced = failed.MainService?.TargetId == "primary-target" && failed.MainService.Status == ServiceStatus.Ready &&
        failed.InstanceDiagnostics.Single(x => x.InstanceId == "auxiliary").State == "Unavailable" && failed.InstanceDiagnostics.Single(x => x.InstanceId == "auxiliary").LastFailure is not null &&
        !failed.AdmitReasons.Any(x => x.StartsWith("Wcs", StringComparison.Ordinal)) && control.MainService?.Status == ServiceStatus.Ready;
    var report = new { AuditSha = sourceSha, Result = reproduced ? "FIX_VERIFIED" : "FIX_FAILED", PrimaryUrl = primaryUrl,
        AuxiliaryUrl = auxiliaryUrl, Cases = new[] { failed, control }, HealthCalls = healthCalls, EffectCalls = effectCalls,
        Windows = "NOT_RUN", DeviceActions = "NONE", RepositoryMutations = "NONE" };
    File.WriteAllText(Path.Combine(evidence, "health-result.json"), Wire.Encode(report));
    Console.WriteLine(Wire.Encode(report));
    if (!reproduced || effectCalls != 0) Environment.ExitCode = 3;
}
finally
{
    receiver.Stop();
    await server;
}

sealed record CaseResult(string Case, int PrimaryHealthRequests, ServiceState? MainService, IntegrationDiagnostic[] InstanceDiagnostics, string[] AdmitReasons, string[] StartupDiagnostics);
