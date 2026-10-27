using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

internal static class HikrobotSdkEditing
{
    internal static void Run(string workspace, string root)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); Console.WriteLine("PASS SDK02: " + reason); }
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var catalog = new ParcelCapabilityFactory(new(Path.Combine(workspace, "config/sportmaster"), root, null, null)).Catalog();
        var fields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var package = new PackageReference("hikrobot.idmvs-reader", "1.0.0", new('a', 64));
        var sdk = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", package.Version, "HIKROBOT-IDMVS-OBSERVATION-v1", fields, new(), package, "idmvs-reader");
        catalog = catalog with { Providers = [.. catalog.Providers, sdk], Packages = [new(package, [])] };
        var draft = new StartupConfigurationDraft(config, catalog);
        var left = draft.Readers[0]; var right = draft.Readers[1];
        var tcp = catalog.Providers.Single(p => p.Kind == "observation" && p.Implementation == "tcp-line");
        foreach (var (reader, index) in draft.Readers.Select((r, i) => (r, i)))
        {
            reader.Provider = tcp;
            reader.Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2." + (10 + index);
            reader.Parameters.Single(p => p.Field.Key == "device.port").Value = "9000";
            reader.Parameters.Single(p => p.Field.Key == "bcr-code").Value = "SYNTH-HB-" + index;
        }
        var rightBefore = Wire.Encode(right.CaptureRaw()); var leftBefore = Wire.Encode(left.CaptureRaw());
        left.ProposeProvider(sdk);
        Check(left.Provider == tcp && left.ProviderChangePending && left.Parameters.Any(p => p.Field.Key == "device.port"), "proposal keeps current input until explicit decision");
        var editor = draft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c)));
        _ = editor.ValidateFields();
        Check(editor.Issues.Any(i => i.Path == left.InstanceId + "/Provider"), "pending choice blocks save at this provider field");
        try { _ = left.Build(); throw new Exception("Pending choice saved"); } catch (InvalidOperationException ex) when (ex.Message.Contains("/Provider")) { }
        var restored = draft.WithRestored(draft.Capture());
        Check(restored.Readers[0].ProposedProvider == sdk && restored.Readers[0].Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.10", "startup draft retains pending choice and original input");
        var raw = CapabilityDraft.RestoreRaw(Wire.Decode<CapabilityDraft.Saved>(Wire.Encode(left.CaptureRaw())), catalog.Providers);
        Check(raw.ProposedProvider == sdk && raw.Parameters.Single(p => p.Field.Key == "device.port").Value == "9000", "daily draft round-trip preserves pending choice without applying it");
        left.CancelProviderChange();
        Check(Wire.Encode(left.CaptureRaw()) == leftBefore && Wire.Encode(right.CaptureRaw()) == rightBefore, "cancel preserves both cameras exactly");
        left.ProposeProvider(sdk); left.ApplyProviderChange();
        Check(left.Provider == sdk && !left.ProviderChangePending && left.Parameters.All(p => !tcp.Parameters.Any(t => t.Key == p.Field.Key && t.Key.StartsWith("tcp.")) && p.Field.Key != "device.port") &&
            left.Parameters.Single(p => p.Field.Key == "device.ip").Value == "" && left.Parameters.Single(p => p.Field.Key == "bcr-code").Value == "", "confirmed SDK choice uses new schema defaults and clears TCP fields");
        left.Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2.20";
        left.Parameters.Single(p => p.Field.Key == "bcr-code").Value = "SYNTH-SDK-HB";
        var binding = left.BuildReader("sportmaster");
        Check(binding.Parameters["entity.mode"] == "observe-only" && binding.Parameters["queue.maximum-code-bytes"] == "16777216" && Wire.Encode(right.CaptureRaw()) == rightBefore,
            "SDK candidate stays observe-only with byte budget and independent right camera");
        left.Parameters.Single(p => p.Field.Key == "queue.maximum-code-bytes").Value = "0"; _ = editor.ValidateFields();
        Check(editor.Issues.Any(i => i.Path == left.InstanceId + "/queue.maximum-code-bytes"), "invalid byte budget is retained and field-addressable");
        left.ProposeProvider(tcp); left.ApplyProviderChange();
        Check(left.Parameters.All(p => p.Field.Key is not ("device.serial" or "queue.maximum-code-bytes")) && left.Parameters.Single(p => p.Field.Key == "device.ip").Value == "",
            "another explicit selection resets schema without restoring prior provider values");
        var mvsFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Drivers.Hikrobot.Mvs/parameters.json")));
        Check(mvsFields.All(p => p.Key is not ("device.port" or "code.encoding" or "queue.capacity")) &&
            mvsFields.Single(p => p.Key == "entity.mode").Choices!.SequenceEqual(["observe-only"]), "MVS acquisition schema has no invented barcode encoding, TCP transport or production association");
        Console.WriteLine("SDK_EDITING_PASS; synthetic descriptors only; real SDK/model/Production qualification NOT_RUN");
    }
}
