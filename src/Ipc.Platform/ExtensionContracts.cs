using System.Globalization;
using System.Text.RegularExpressions;

namespace Ipc.Platform;

// All discovery data is signed, declarative metadata. Reading a schema never loads code.
public sealed record ExtensionFile(string Path, long Length, string Sha256);
public sealed record PackageReference(string Id, string Version, string ManifestSha256);
public sealed record ExtensionPrerequisite(string Kind, string Identity, string Version, string Evidence);
public sealed record ParameterField(string Key, string Group, string Label, string Type, bool Required = false,
    string? Default = null, int MaximumLength = 256, long? Minimum = null, long? Maximum = null,
    string[]? Choices = null, bool Sensitive = false, string? Description = null, string? Unit = null,
    bool ReadOnly = false, string ApplyLevel = "Restart", string EditRole = "Maintenance",
    string? RequiredWhenKey = null, string? RequiredWhenEquals = null, int MaximumItems = 32);
public sealed record ExtensionModule(string Id, string Assembly, string EntryType, string Kind,
    int MaximumInstances, ParameterField[] Parameters,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CapabilityRequirements? Requirements = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ContractIdentity = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ProviderAssurance? Assurance = null);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ExtensionManifest(int SchemaVersion, string Id, string Version, string Publisher,
    string Runtime, string Rid, int ContractMajor, RunMode Mode, string SourceCommit,
    PackageReference[] Dependencies, ExtensionFile[] Files, ExtensionModule[] Modules,
    ExtensionPrerequisite[] Prerequisites, string HostContractSha256 = "",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string>? PublicContracts = null)
{
    public ExtensionManifest(int schemaVersion, string id, string version, string publisher, string runtime, string rid,
        int contractMajor, RunMode mode, string sourceCommit, PackageReference[] dependencies, ExtensionFile[] files,
        ExtensionModule[] modules, ExtensionPrerequisite[] prerequisites, string hostContractSha256)
        : this(schemaVersion, id, version, publisher, runtime, rid, contractMajor, mode, sourceCommit, dependencies, files, modules, prerequisites, hostContractSha256, null) { }
}
public sealed record PublisherTrust(string Id, string PublicKeyPem, bool TestOnly);
public sealed record ExtensionTrust(int SchemaVersion, PublisherTrust[] Publishers);
public sealed record ExtensionInstance(string Id, string PackageId, string ModuleId, string DeviceId,
    bool Enabled, Dictionary<string, string> Parameters);
public sealed record ServiceInstance(string Id, string PackageId, string ModuleId, bool Enabled, Dictionary<string, string> Parameters);
public sealed record ServicePortBinding(string Port, string InstanceId, IntegrationBinding Target);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record DeploymentExtensions(PackageReference[] Packages, ExtensionInstance[] Instances,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ServiceInstance[]? Services = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ServicePortBinding[]? Ports = null)
{
    public DeploymentExtensions(PackageReference[] packages, ExtensionInstance[] instances) : this(packages, instances, null, null) { }
    public void Deconstruct(out PackageReference[] packages, out ExtensionInstance[] instances) { packages = Packages; instances = Instances; }
}
public sealed record ExtensionContext(string InstanceId, string DeviceId, IReadOnlyDictionary<string, string> Parameters);
public interface IObservationExtension
{
    IControlledObservationSource Create(ExtensionContext context);
}
// Explicit synthetic input used by the existing engineering view. Production never calls it.
public interface ISyntheticObservationInput
{
    bool Emit(string kind, string admissionId, string[] codes, int? quantity, string sourceEventId);
}

public static class ExtensionSchema
{
    public static bool IsId(string? value) => value is { Length: >= 1 and <= 64 } &&
        Regex.IsMatch(value, "^[a-z][a-z0-9.-]*$", RegexOptions.CultureInvariant);
    public static bool IsVersion(string? value) => value is { Length: >= 5 and <= 32 } &&
        Regex.IsMatch(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    public static bool IsHash(string? value) => value?.Length == 64 && value.All(char.IsAsciiHexDigit);
    public static string[] ValidateDefinition(ParameterField[] fields)
    {
        if (fields is null || fields.Length > 100 || fields.Any(x => x is null)) return ["Invalid parameter schema"];
        List<string> errors = [];
        if (fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != fields.Length) errors.Add("Duplicate parameter keys");
        foreach (var f in fields)
        {
            if (!IsId(f.Key) || f.Key.Split('.').Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(f.Group) || f.Group.Length > 80 || f.Group.Split('/').Any(string.IsNullOrWhiteSpace) ||
                string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 120 || f.Description?.Length > 1000 ||
                f.Type is not ("string" or "integer" or "boolean" or "choice" or "secret-reference" or "string-list") ||
                f.MaximumLength is < 1 or > 4096 || f.Minimum > f.Maximum ||
                f.Type != "integer" && (f.Minimum is not null || f.Maximum is not null) ||
                f.ApplyLevel != "Restart" || f.EditRole != "Maintenance" || f.Unit?.Length > 32 || f.MaximumItems is < 1 or > 128 ||
                f.RequiredWhenKey is not null && (!fields.Any(x => x.Key == f.RequiredWhenKey && x.Key != f.Key) || f.RequiredWhenEquals is null) ||
                f.Sensitive != (f.Type == "secret-reference") ||
                f.Type == "choice" && (f.Choices is null || f.Choices.Length is < 1 or > 100 ||
                    f.Choices.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > f.MaximumLength) || f.Choices.Distinct().Count() != f.Choices.Length) ||
                f.Type != "choice" && f.Choices is not null || f.Sensitive && f.Default is not null)
                errors.Add("Invalid parameter definition: " + f.Key);
            if (f.Default is not null) errors.AddRange(ValidateValue(f, f.Default));
        }
        return errors.ToArray();
    }
    public static string[] Validate(ParameterField[] fields, IReadOnlyDictionary<string, string> values)
    {
        List<string> errors = [.. ValidateDefinition(fields)];
        if (errors.Count > 0) return errors.ToArray();
        if (values is null) return [.. errors, "Missing parameter values"];
        if (values.Keys.Any(x => !fields.Any(f => f.Key == x))) errors.Add("Unknown parameter");
        foreach (var f in fields)
        {
            var value = values.GetValueOrDefault(f.Key) ?? f.Default;
            var conditionValue = f.RequiredWhenKey is null ? null : values.GetValueOrDefault(f.RequiredWhenKey) ?? fields.FirstOrDefault(x => x.Key == f.RequiredWhenKey)?.Default;
            if (string.IsNullOrEmpty(value)) { if (f.Required || f.RequiredWhenKey is not null && conditionValue == f.RequiredWhenEquals) errors.Add(f.Key + ": required"); continue; }
            if (f.ReadOnly && value != f.Default) errors.Add(f.Key + ": read-only schema value changed");
            errors.AddRange(ValidateValue(f, value));
        }
        return errors.ToArray();
    }
    static IEnumerable<string> ValidateValue(ParameterField f, string value)
    {
        var valid = f.Type == "string-list"
            ? value.Length <= f.MaximumLength * f.MaximumItems + 8 * f.MaximumItems
            : value.Length <= f.MaximumLength && !value.Any(char.IsControl);
        valid &= f.Type switch
        {
            "integer" => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) &&
                (f.Minimum is null || n >= f.Minimum) && (f.Maximum is null || n <= f.Maximum),
            "boolean" => value is "true" or "false",
            "choice" => f.Choices?.Contains(value, StringComparer.Ordinal) == true,
            "secret-reference" => Regex.IsMatch(value, "^credential:[a-zA-Z0-9._-]{1,128}$", RegexOptions.CultureInvariant),
            "string" => true,
            "string-list" => ValidList(f, value),
            _ => false
        };
        if (!valid) yield return f.Key + ": invalid value (value omitted)";
    }
    static bool ValidList(ParameterField field, string value)
    {
        try
        {
            var items = Wire.Decode<string[]>(value);
            return items.Length <= field.MaximumItems && items.All(x => x is not null && x.Length <= field.MaximumLength && !x.Any(char.IsControl));
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
    public static Dictionary<string, string> EffectiveValues(ExtensionModule module, ExtensionInstance instance) =>
        module.Parameters.Where(x => instance.Parameters.ContainsKey(x.Key) || x.Default is not null)
            .ToDictionary(x => x.Key, x => instance.Parameters.GetValueOrDefault(x.Key) ?? x.Default!, StringComparer.Ordinal);
}
