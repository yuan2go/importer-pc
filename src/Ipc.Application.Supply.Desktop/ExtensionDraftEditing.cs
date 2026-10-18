using System.IO;
using Ipc.Platform;

namespace Ipc.Desktop;

// Per-window draft editing only. Persistence, authorization and deployment CAS stay
// in the application contract. Renaming replaces the original identity atomically.
internal static class ExtensionDraftEditing
{
    public static DeploymentExtensions SaveService(DeploymentExtensions draft, string? originalId,
        ServiceInstance service, ServicePortBinding? binding)
    {
        var services = draft.Services ?? [];
        RequireIdentity(originalId, service.Id, services.Select(x => x.Id), draft.Instances.Select(x => x.Id));
        if (service.Enabled != (binding is not null) || binding is not null && binding.InstanceId != service.Id)
            throw new InvalidDataException("Enabled service requires its own port binding");
        var ports = (draft.Ports ?? []).Where(x => x.InstanceId != originalId).ToArray();
        if (binding is not null && ports.Any(x => x.Port == binding.Port))
            throw new InvalidOperationException("Port already belongs to another service: " + binding.Port);
        return draft with
        {
            Services = services.Where(x => x.Id != originalId).Append(service).ToArray(),
            Ports = binding is null ? ports : [.. ports, binding]
        };
    }

    public static DeploymentExtensions SaveDevice(DeploymentExtensions draft, string? originalId, ExtensionInstance instance)
    {
        RequireIdentity(originalId, instance.Id, draft.Instances.Select(x => x.Id), (draft.Services ?? []).Select(x => x.Id));
        return draft with { Instances = draft.Instances.Where(x => x.Id != originalId).Append(instance).ToArray() };
    }

    static void RequireIdentity(string? originalId, string newId, IEnumerable<string> sameKind, IEnumerable<string> otherKind)
    {
        if (!ExtensionSchema.IsId(newId)) throw new InvalidDataException("Invalid instance ID");
        var existing = sameKind.ToArray();
        if (originalId is not null && !existing.Contains(originalId))
            throw new InvalidOperationException("Original selected instance is no longer in this draft; select it again");
        if (existing.Any(x => x == newId && x != originalId) || otherKind.Contains(newId))
            throw new InvalidOperationException("Instance ID already belongs to another instance: " + newId);
    }
}
