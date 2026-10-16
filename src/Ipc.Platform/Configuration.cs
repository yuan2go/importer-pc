using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ipc.Platform;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("Null document");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    public static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static string Id() => Guid.NewGuid().ToString("N");
}

public enum RunMode { Simulation, Production }
public sealed record ModuleSpec(string Id, string Kind, string Version, int ContractMajor, string[] Dependencies, string File, string Sha256);
public sealed record ProductManifest(int SchemaVersion, string ProductId, RunMode Mode, string Runtime, string Architecture,
    string BusinessModule, string[] Views, ModuleSpec[] Modules);
public sealed record DeviceBinding(string Role, string Driver, string DeviceId, string Revision, string? ReplayFile = null);
public sealed record IntegrationBinding(string Id, string Revision, string Contract, string Endpoint);
public sealed record StationConfig(int SchemaVersion, string StationId, string Version, RunMode Mode, DeviceBinding[] Devices,
    IntegrationBinding Integration, int EventCapacity = 64, int CommandCapacity = 32, int OutboxCapacity = 64,
    int TimeoutMs = 1500, int MinimumFreeMb = 128, string Policy = "SYNTHETIC-KEEP-v1", int FixtureDemand = 120,
    int MaximumPackage = 1000);
public sealed record PresentationSettings(string View = "workstation", string Layout = "standard");

public static class ProductValidation
{
    static readonly Dictionary<string, string> Approved = new()
    {
        ["platform"] = "Ipc.Platform.dll", ["supply"] = "Ipc.Supply.dll", ["adapters"] = "Ipc.Adapters.dll", ["product"] = "Ipc.Product.dll"
    };
    public static IReadOnlyList<string> Validate(ProductManifest p, StationConfig s, PresentationSettings v, string installRoot)
    {
        var errors = new List<string>();
        if (p.SchemaVersion != 1 || s.SchemaVersion != 1 || p.Runtime != "net10.0" || p.BusinessModule != "supply" || p.ProductId != "ipc-reference") errors.Add("Unsupported product/schema/runtime/business");
        if (p.Mode != s.Mode) errors.Add("Mode mismatch");
        if (p.Architecture != "any" && !string.Equals(p.Architecture, RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase)) errors.Add("Process architecture mismatch");
        if (!p.Views.Contains(v.View) || v.Layout is not ("standard" or "compact")) errors.Add("View/layout not approved");
        if (p.Modules.Select(x => x.Id).Distinct().Count() != p.Modules.Length || p.Modules.Length != Approved.Count) errors.Add("Duplicate/missing modules");
        var seen = new HashSet<string>(); var visiting = new HashSet<string>();
        bool Visit(string id)
        {
            if (seen.Contains(id)) return true;
            var module = p.Modules.FirstOrDefault(x => x.Id == id);
            if (module is null || !visiting.Add(id)) return false;
            foreach (var dep in module.Dependencies) if (!Visit(dep)) return false;
            visiting.Remove(id); seen.Add(id); return true;
        }
        foreach (var m in p.Modules)
        {
            if (!Approved.TryGetValue(m.Id, out var expected) || m.File != expected || m.Version != "1.0.0" || m.ContractMajor != 1 || m.Kind != m.Id) errors.Add($"Unapproved module/contract: {m.Id}");
            if (!Visit(m.Id)) errors.Add($"Missing dependency/cycle: {m.Id}");
            var path = Path.GetFullPath(Path.Combine(installRoot, m.File));
            if (Path.IsPathRooted(m.File) || m.File.Contains("..") || Path.GetDirectoryName(path) != Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar)) errors.Add($"Unsafe module path: {m.Id}");
            else if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || Wire.FileHash(path) != m.Sha256) errors.Add($"Missing/changed module: {m.Id}");
        }
        errors.AddRange(ValidateStation(s));
        return errors;
    }
    public static IReadOnlyList<string> ValidateStation(StationConfig s)
    {
        var e = new List<string>();
        if (s.SchemaVersion != 1 || string.IsNullOrWhiteSpace(s.Version) || s.StationId.Length is < 1 or > 60 || s.StationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) e.Add("Invalid station identity/version");
        if (s.EventCapacity is < 1 or > 4096 || s.CommandCapacity is < 1 or > 1024 || s.OutboxCapacity is < 1 or > 4096 || s.TimeoutMs is < 100 or > 30000 || s.MinimumFreeMb < 1) e.Add("Invalid capacity/time budget");
        if (s.Devices.Length != 2 || !s.Devices.Select(x => x.Role).Order().SequenceEqual(new[] { "left", "right" }) || s.Devices.Select(x => x.DeviceId).Distinct().Count() != 2) e.Add("Reference product requires two unique logical bindings");
        if (s.Devices.Any(x => string.IsNullOrWhiteSpace(x.Revision) || string.IsNullOrWhiteSpace(x.DeviceId))) e.Add("Incomplete binding");
        if (s.Mode == RunMode.Simulation)
        {
            if (s.Devices.Any(x => x.Driver is not ("manual" or "replay"))) e.Add("Unknown simulation driver");
            if (!Uri.TryCreate(s.Integration.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "") e.Add("Fixture requires literal IPv4 loopback root HTTP endpoint");
            if (s.Integration.Contract != "SIMULATION-v1" || s.Policy != "SYNTHETIC-KEEP-v1" || s.FixtureDemand is < 1 or > 100000 || s.MaximumPackage is < 1 or > 10000) e.Add("Invalid synthetic policy/contract");
        }
        else e.Add("Production NotReady: approved SDK, WCS contract, quantity/entity policy and identity provider unavailable");
        return e;
    }
}
