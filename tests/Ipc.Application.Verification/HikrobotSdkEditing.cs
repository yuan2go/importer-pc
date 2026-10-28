using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
internal static class HikrobotSdkEditing
{
    internal static void Run(string workspace, string root)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); Console.WriteLine("PASS BUNDLED SDK: " + reason); }
        var config = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration();
        var catalog = new ParcelCapabilityFactory(new(Path.Combine(workspace, "config/sportmaster"), root, null, null)).Catalog();
        var draft = new StartupConfigurationDraft(config, catalog);
        Check(draft.Readers.All(r => r.Provider?.Implementation == "hikrobot-mvs-reader" && r.Provider.Package is null), "fresh candidate resolves two real built-in SDK readers without extension setup");
        Check(catalog.Providers.Count(p => p.Implementation == "hikrobot-mvs-reader") == 1, "one current SDK descriptor is registered");
        foreach (var (reader, i) in draft.Readers.Select((r, i) => (r, i))) {
            reader.Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2." + (10+i);
            reader.Parameters.Single(p => p.Field.Key == "device.serial").Value = "SYNTH-" + i;
            Check(reader.Parameters.All(p => p.Field.Key is not ("device.port" or "bcr-code")), "SDK schema excludes TCP and duplicate code");
        }
        var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
        Check(rcs.Parameters.All(p => p.Field.Key is not ("source" or "locale")), "RCS envelope constants are not parameters");
        rcs.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value = "SYNTH-LEFT";
        rcs.Parameters.Single(p => p.Field.Key == "right-bcr-code").Value = "SYNTH-RIGHT";
        var candidate = draft.Build();
        Check(candidate.Topology!.Readers.All(r => r.Driver == "hikrobot-mvs-reader" && r.Parameters is not null) && candidate.Extensions is null, "save preserves SDK parameters without extension deployment");
        var restored = new StartupConfigurationDraft(candidate, catalog);
        Check(restored.Readers[0].Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.10" && restored.Readers[1].Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.11", "reload isolates left and right");
        var active = candidate with { DeferredConnections = [] }; ParcelCapabilityFactory.ValidateHikrobotIdentities(active);
        var changed = active.Topology!.Readers.Select((r,i) => i == 1 ? r with { Parameters = new(r.Parameters!) { ["device.serial"] = "SYNTH-0" } } : r).ToArray();
        try { ParcelCapabilityFactory.ValidateHikrobotIdentities(active with { Reader=changed[0], Topology=active.Topology with { Readers=changed } }); throw new Exception("Duplicate accepted"); }
        catch (InvalidDataException) { Check(true, "built-in duplicate physical identity rejected"); }
        Console.WriteLine("BOUNDARY model only; native SDK/frame/barcode/site NOT_RUN");
    }
}
