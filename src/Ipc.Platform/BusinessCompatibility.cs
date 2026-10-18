using System.Reflection;
using System.Security.Cryptography;
namespace Ipc.Platform;

// Exact provider identities; signature, dependency and semantic checks remain independent.
public sealed record ProviderVersion(string Kind, string Implementation, string Version, string Contract);
public sealed record CompatibleProviderSet(string Id, ProviderVersion[] Providers);
public sealed record BusinessCompatibility(int SchemaVersion, string ApplicationId, CompatibleProviderSet[] ProviderSets)
{
    public const string ResourceName = "Ipc.BusinessCompatibility.json";
    public static BusinessCompatibility Read(Assembly assembly)
    {
        using var source = assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidDataException("Business compatibility declaration missing");
        using var reader = new StreamReader(source);
        var declaration = Wire.Decode<BusinessCompatibility>(reader.ReadToEnd());
        declaration.Validate(); return declaration;
    }
    public static string Fingerprint(Assembly assembly)
    {
        using var source = assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidDataException("Business compatibility declaration missing");
        return Convert.ToHexString(SHA256.HashData(source));
    }
    public static string VersionOf(Assembly assembly)
    {
        var v = assembly.GetName().Version ?? throw new InvalidDataException("Business assembly version missing");
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || !ExtensionSchema.IsId(ApplicationId) || ProviderSets is not { Length: > 0 and <= 32 } ||
            ProviderSets.Any(s => s is null || !ExtensionSchema.IsId(s.Id) || s.Providers is not { Length: > 0 and <= 128 }) ||
            ProviderSets.Select(s => s.Id).Distinct().Count() != ProviderSets.Length)
            throw new InvalidDataException("Invalid business compatibility declaration");
        foreach (var set in ProviderSets)
            if (set.Providers.Any(p => p is null || p.Kind is not ("observation" or "integration" or "plc" or "weight" or "camera" or "upload" or "login") ||
                string.IsNullOrWhiteSpace(p.Implementation) || p.Implementation.Contains('*') || !ExtensionSchema.IsVersion(p.Version) ||
                string.IsNullOrWhiteSpace(p.Contract) || p.Contract.Contains('*')) || set.Providers.Distinct().Count() != set.Providers.Length)
                throw new InvalidDataException("Invalid exact provider compatibility set: " + set.Id);
    }
    public string Require(IEnumerable<ProviderVersion> selected)
    {
        Validate(); var providers = selected.Distinct().ToArray();
        if (providers.Length == 0) throw new InvalidDataException("No selected providers");
        return ProviderSets.FirstOrDefault(s => providers.All(p => s.Providers.Contains(p)))?.Id ??
            throw new InvalidDataException("Selected provider combination is not declared compatible with business " + ApplicationId);
    }
}
