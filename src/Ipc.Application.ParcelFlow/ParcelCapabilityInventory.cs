using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public static partial class ParcelConfigurationRules
{
    // Inspect original declarations before All/Bound removes role/inventory mirrors.
    // Format validation is shared by live configuration and offline responsibility
    // readers; it never loads providers or requires a package to remain installed.
    public static string[] ValidateCapabilityInventory(ParcelConfiguration configuration)
    {
        if (configuration.SchemaVersion is not (1 or 2 or 3)) return ["Unsupported capability inventory schema"];
        var inventory = configuration.CapabilityInstances;
        if (inventory is { Length: > 64 } || inventory?.Any(b => b is null) == true)
            return ["Invalid bounded capability instance inventory"];
        var roles = new[] { configuration.Scale, configuration.Wcs, configuration.Plc, configuration.Login,
            configuration.MaintenanceLogin, configuration.Images?.Camera, configuration.Images?.Uploader }.OfType<CapabilityBinding>().ToArray();
        List<string> errors = [];
        if (roles.Select(b => b.InstanceId).Distinct(StringComparer.Ordinal).Count() != roles.Length)
            errors.Add("Capability roles cannot reuse the same instance identity");
        if (roles.Any(b => !b.Enabled)) errors.Add("A bound capability must be enabled");
        if (inventory is not null)
        {
            if (inventory.Select(b => b.InstanceId).Distinct(StringComparer.Ordinal).Count() != inventory.Length)
                errors.Add("Capability inventory identities must be unique before deduplication");
            if (roles.Any(role => !inventory.Any(b => Wire.Encode(b) == Wire.Encode(role))))
                errors.Add("Capability inventory must contain each complete authoritative role binding");
            if (roles.Any(role => inventory.Any(b => b.InstanceId == role.InstanceId && Wire.Encode(b) != Wire.Encode(role))))
                errors.Add("Role and inventory identity have contradictory binding content");
        }
        var deployment = configuration.Extensions;
        if (deployment is not null && (deployment.Packages is null || deployment.Instances is null ||
            deployment.Packages.Length > 64 || deployment.Instances.Length > 64 ||
            deployment.Packages.Any(p => p is null || !ExtensionSchema.IsId(p.Id) || !ExtensionSchema.IsVersion(p.Version) || !ExtensionSchema.IsHash(p.ManifestSha256)) ||
            deployment.Packages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != deployment.Packages.Length ||
            deployment.Instances.Any(i => i is null || !ExtensionSchema.IsId(i.Id) || !ExtensionSchema.IsId(i.PackageId) || !ExtensionSchema.IsId(i.ModuleId) || string.IsNullOrWhiteSpace(i.DeviceId) || i.Parameters is null) ||
            deployment.Instances.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != deployment.Instances.Length ||
            deployment.Services is { Length: > 0 } || deployment.Ports is { Length: > 0 }))
            return [.. errors, "Invalid exact capability deployment closure"];
        foreach (var binding in roles.Concat(inventory ?? []))
        {
            var deferredRcs = configuration.ApplicationId == "sportmaster" && binding.InstanceId == configuration.Wcs?.InstanceId &&
                binding.Contract == "SPORTMASTER-RCS-SORTER-v1" && ParcelConnectionSetup.IsDeferred(configuration, binding.InstanceId);
            if (!ExtensionSchema.IsId(binding.InstanceId) || !ExtensionSchema.IsVersion(binding.Version) ||
                binding.Kind is not ("weight" or "plc" or "integration" or "login" or "camera" or "upload") ||
                string.IsNullOrWhiteSpace(binding.Target) && !deferredRcs || string.IsNullOrWhiteSpace(binding.Contract) ||
                string.IsNullOrWhiteSpace(binding.Implementation) || binding.Parameters is null)
            { errors.Add("Incomplete or unknown capability inventory identity"); continue; }
            if (binding.Package is { } package
                ? !ExtensionSchema.IsId(package.Id) || !ExtensionSchema.IsVersion(package.Version) || !ExtensionSchema.IsHash(package.ManifestSha256) ||
                    deployment?.Packages.Contains(package) != true || binding.Version != package.Version || !ExtensionSchema.IsId(binding.Module) ||
                    binding.Implementation != "extension:" + package.Id + "/" + binding.Module
                : binding.Module is not null || binding.Implementation.StartsWith("extension:", StringComparison.Ordinal))
                errors.Add("Dangling or contradictory exact capability package reference");
        }
        if (deployment?.Instances.Any(i => !deployment.Packages.Any(p => p.Id == i.PackageId)) == true)
            errors.Add("Observation instance package absent from exact deployment");
        return errors.ToArray();
    }
}
