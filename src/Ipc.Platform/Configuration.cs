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

// Explicit, trusted registration supplied by the installed product. The platform does not
// discover arbitrary assemblies or infer a business from a filename.
public sealed record ModuleRegistration(string Id, string Kind, string Version, int ContractMajor, string[] Dependencies, string File);
public sealed record DriverRegistration(string Id, RunMode Mode);
public sealed record ProductDefinition(string ProductId, string Runtime, string BusinessModule, RunMode[] Modes,
    ModuleRegistration[] Modules, string[] Views, string[] Layouts, DriverRegistration[] Drivers);

public static class ProductValidation
{
    public static IReadOnlyList<string> Validate(ProductDefinition definition, ProductManifest p, StationConfig s, PresentationSettings v, string installRoot)
    {
        var errors = new List<string>();
        if (definition.Modules is null || definition.Views is null || definition.Layouts is null || definition.Drivers is null || definition.Modes is null ||
            definition.Modules.Any(x => x is null || x.Dependencies is null) || definition.Drivers.Any(x => x is null) ||
            p.Modules is null || p.Views is null || p.Modules.Any(x => x is null || x.Dependencies is null || x.Sha256 is null) ||
            s.Devices is null || s.Devices.Any(x => x is null) || s.Integration is null)
        { errors.Add("Incomplete product/station registration"); return errors; }
        if (string.IsNullOrWhiteSpace(definition.ProductId) || definition.Runtime != $"net{Environment.Version.Major}.{Environment.Version.Minor}" ||
            definition.Modules.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Kind) || string.IsNullOrWhiteSpace(x.Version) || x.ContractMajor < 1 ||
                x.Dependencies.Any(string.IsNullOrWhiteSpace) || x.Dependencies.Distinct().Count() != x.Dependencies.Length) ||
            definition.Modules.Select(x => x.File).Distinct().Count() != definition.Modules.Length ||
            definition.Views.Length == 0 || definition.Layouts.Length == 0 || definition.Views.Any(string.IsNullOrWhiteSpace) || definition.Layouts.Any(string.IsNullOrWhiteSpace) ||
            definition.Views.Distinct().Count() != definition.Views.Length || definition.Layouts.Distinct().Count() != definition.Layouts.Length ||
            definition.Modes.Length == 0 || definition.Modes.Any(x => !Enum.IsDefined(x)) || definition.Modes.Distinct().Count() != definition.Modes.Length ||
            definition.Drivers.Any(x => string.IsNullOrWhiteSpace(x.Id) || !definition.Modes.Contains(x.Mode)) || definition.Drivers.Distinct().Count() != definition.Drivers.Length)
            errors.Add("Malformed trusted product registration");
        if (p.SchemaVersion != 1 || p.ProductId != definition.ProductId || p.Runtime != definition.Runtime || p.BusinessModule != definition.BusinessModule)
            errors.Add("Unsupported product/schema/runtime/business registration");
        if (!Enum.IsDefined(p.Mode) || !definition.Modes.Contains(p.Mode) || p.Mode != s.Mode) errors.Add("Mode mismatch/unregistered mode");
        if (p.Architecture != "any" && !string.Equals(p.Architecture, RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase)) errors.Add("Process architecture mismatch");
        if (p.Views.Length == 0 || p.Views.Distinct().Count() != p.Views.Length || p.Views.Any(x => !definition.Views.Contains(x)) ||
            !p.Views.Contains(v.View) || !definition.Layouts.Contains(v.Layout)) errors.Add("View/layout not approved");
        if (definition.Modules.Length == 0 || definition.Modules.Select(x => x.Id).Distinct().Count() != definition.Modules.Length ||
            !definition.Modules.Any(x => x.Id == definition.BusinessModule)) errors.Add("Invalid trusted product registration");
        if (p.Modules.Select(x => x.Id).Distinct().Count() != p.Modules.Length ||
            !p.Modules.Select(x => x.Id).Order().SequenceEqual(definition.Modules.Select(x => x.Id).Order())) errors.Add("Duplicate/missing/unregistered modules");
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
            var expected = definition.Modules.FirstOrDefault(x => x.Id == m.Id);
            if (expected is null || m.Kind != expected.Kind || m.File != expected.File || m.Version != expected.Version ||
                m.ContractMajor != expected.ContractMajor || m.Dependencies.Distinct().Count() != m.Dependencies.Length ||
                !m.Dependencies.Order().SequenceEqual(expected.Dependencies.Order())) errors.Add($"Unapproved module/contract: {m.Id}");
            if (!Visit(m.Id)) errors.Add($"Missing dependency/cycle: {m.Id}");
            if (!IsSafeRelativeFile(installRoot, m.File, out var path)) errors.Add($"Unsafe module path: {m.Id}");
            else
            {
                try
                {
                    if (!File.Exists(path) || m.Sha256.Length != 64 || Wire.FileHash(path) != m.Sha256) errors.Add($"Missing/changed module: {m.Id}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"Unreadable module: {m.Id}"); }
            }
        }
        errors.AddRange(ValidateStationStructure(s));
        foreach (var binding in s.Devices)
            if (!definition.Drivers.Any(x => x.Id == binding.Driver && x.Mode == s.Mode)) errors.Add($"Unregistered driver/mode: {binding.Driver}");
        return errors;
    }
    public static bool IsSafeRelativeFile(string root, string relative, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(x => x is "" or "." or "..")) return false;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)) return false;
        // Reject symlinks/reparse points at every component, not only the assembly itself.
        var current = fullRoot;
        try
        {
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return false;
            foreach (var component in relative.Split('/'))
            {
                current = Path.Combine(current, component);
                if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        return true;
    }
    public static IReadOnlyList<string> ValidateStationStructure(StationConfig s)
    {
        var e = new List<string>();
        if (s.Devices is null || s.Devices.Any(x => x is null) || s.Integration is null || s.StationId is null)
        { e.Add("Incomplete station structure"); return e; }
        if (s.SchemaVersion != 1 || !Enum.IsDefined(s.Mode) || string.IsNullOrWhiteSpace(s.Version) || s.StationId.Length is < 1 or > 60 || s.StationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) e.Add("Invalid station identity/version/mode");
        if (s.EventCapacity is < 1 or > 4096 || s.CommandCapacity is < 1 or > 1024 || s.OutboxCapacity is < 1 or > 4096 || s.TimeoutMs is < 100 or > 30000 || s.MinimumFreeMb < 1) e.Add("Invalid capacity/time budget");
        if (s.Devices.Select(x => x.Role).Distinct().Count() != s.Devices.Length || s.Devices.Select(x => x.DeviceId).Distinct().Count() != s.Devices.Length) e.Add("Duplicate logical/device binding");
        if (s.Devices.Any(x => string.IsNullOrWhiteSpace(x.Role) || string.IsNullOrWhiteSpace(x.Driver) || string.IsNullOrWhiteSpace(x.Revision) || string.IsNullOrWhiteSpace(x.DeviceId))) e.Add("Incomplete binding");
        if (string.IsNullOrWhiteSpace(s.Integration.Id) || string.IsNullOrWhiteSpace(s.Integration.Revision) || string.IsNullOrWhiteSpace(s.Integration.Contract) || !Uri.TryCreate(s.Integration.Endpoint, UriKind.Absolute, out _)) e.Add("Incomplete integration binding");
        return e;
    }
}
