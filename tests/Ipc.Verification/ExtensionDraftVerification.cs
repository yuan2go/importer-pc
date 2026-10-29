using Ipc.Desktop;
using Ipc.Platform;

static class ExtensionDraftVerification
{
    public static void Run()
    {
        var target = new IntegrationBinding("fixture", "r1", "SIMULATION-v1", "http://127.0.0.1:8091/");
        var original = new ServiceInstance("service-a", "reference.integration", "reference-http", true, new() { ["metadata.label"] = "A" });
        var other = original with { Id = "service-b", Parameters = new() { ["metadata.label"] = "B" } };
        var device = new ExtensionInstance("camera-a", "reference.barcode", "barcode-source", "SIM-CAMERA", true, []);
        var draft = new DeploymentExtensions([], [device], [original, other], [new("requests", original.Id, target), new("diagnostics", other.Id, target)]);
        var before = Wire.Encode(draft);
        Assert(new[] { draft, new DeploymentExtensions([], [device]) }.All(d => Wire.Encode(Wire.Decode<DeploymentExtensions>(Wire.Encode(d))) == Wire.Encode(d)),
            "current deployment JSON preserves all device/service/port data and supports an explicitly device-only candidate");
        var renamed = original with { Id = "renamed", Parameters = new() { ["metadata.label"] = "new" } };
        var edited = ExtensionDraftEditing.SaveService(draft, original.Id, renamed, new("requests", renamed.Id, target));
        Assert(edited.Services!.Length == 2 && !edited.Services.Any(x => x.Id == original.Id) &&
            edited.Ports!.Single(x => x.Port == "requests").InstanceId == renamed.Id && !edited.Ports!.Any(x => x.InstanceId == original.Id),
            "service rename replaces original service and port without leaving an active copy");
        Assert(edited.Services.Single(x => x.Id == other.Id) == other && edited.Ports!.Single(x => x.Port == "diagnostics").InstanceId == other.Id &&
            Wire.Encode(draft) == before, "service parameters, other targets and effective input stay isolated");
        var changedPort = ExtensionDraftEditing.SaveService(draft, original.Id, renamed, new("new-port", renamed.Id, target));
        Assert(!changedPort.Ports!.Any(x => x.Port == "requests") && changedPort.Ports!.Any(x => x.Port == "new-port"), "renaming with a new port removes original port");
        var disabled = ExtensionDraftEditing.SaveService(draft, original.Id, renamed with { Enabled = false }, null);
        Assert(disabled.Ports!.Length == 1 && disabled.Services!.Single(x => x.Id == renamed.Id).Enabled == false, "disabled renamed service retains no stale binding");
        Refuse(() => ExtensionDraftEditing.SaveService(draft, original.Id, renamed with { Id = other.Id }, new("requests", other.Id, target)), "rename cannot overwrite another service");
        Refuse(() => ExtensionDraftEditing.SaveService(draft, original.Id, renamed with { Id = device.Id }, new("requests", device.Id, target)), "service rename cannot collide with a device");
        Refuse(() => ExtensionDraftEditing.SaveService(draft, original.Id, renamed, new("diagnostics", renamed.Id, target)), "service rename cannot steal another port");
        Refuse(() => ExtensionDraftEditing.SaveService(draft, "missing", renamed, new("requests", renamed.Id, target)), "stale original selection is rejected");
        Refuse(() => ExtensionDraftEditing.SaveService(draft, null, original, new("requests", original.Id, target)), "new instance cannot silently replace an existing service");
        var added = ExtensionDraftEditing.SaveService(draft, null, renamed, new("new-port", renamed.Id, target));
        Assert(added.Services!.Length == 3 && added.Services.Contains(original), "explicit new service preserves both existing instances");
        var updated = ExtensionDraftEditing.SaveService(edited, renamed.Id, renamed, new("requests", renamed.Id, target));
        Assert(updated.Services!.Length == 2 && updated.Ports!.Length == 2, "repeated save updates the new identity without duplication");
        var renamedDevice = device with { Id = "camera-new" };
        var deviceEdit = ExtensionDraftEditing.SaveDevice(draft, device.Id, renamedDevice);
        Assert(deviceEdit.Instances.Single() == renamedDevice && deviceEdit.Services!.Length == 2, "device rename replaces original instance without leaking another device owner");
        Refuse(() => ExtensionDraftEditing.SaveDevice(draft, device.Id, renamedDevice with { Id = original.Id }), "device rename cannot overwrite a service");
        Assert(Wire.Encode(draft) == before, "all rejected edits preserve the complete input draft");
    }
    static void Assert(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException("E13 draft: " + reason); Console.WriteLine("PASS E13 draft " + reason); }
    static void Refuse(Action edit, string reason)
    {
        try { edit(); } catch (InvalidOperationException) { Console.WriteLine("PASS E13 draft refused " + reason); return; }
        throw new Exception("E13 draft accepted: " + reason);
    }
}
