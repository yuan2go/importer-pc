using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraIdentityEditing(CapabilityCatalog catalog, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS CAMERA IDENTITY " + text); }
        var initial = new SportmasterFlow().InitialConfiguration(); var initialBytes = Wire.Encode(initial);
        var draft = new StartupConfigurationDraft(initial, catalog);
        var model = draft.CreateParameterEditor((_, _) => throw new InvalidOperationException("No barcode preview expected"));
        var left = model.Readers[0]; var right = model.Readers[1]; var calls = 0;
        CapabilityParameter Field(CapabilityDraft reader, string key) => reader.Parameters.Single(p => p.Field.Key == key);
        var originalRcs = Wire.Encode(draft.Capabilities.Single(ParameterOwnership.IsRcs).Build());
        model.CameraSerialReader = (ip, engine) =>
        {
            calls++; Check(ip == "192.0.2.10" && engine == "mvid-native", "lookup receives the selected IP and native engine");
            return Task.FromResult("SYNTH-AUTO-LEFT");
        };
        Field(left, "device.ip").Value = "192.0.2.10";
        Check(await model.PrepareCameraIdentitiesAsync() && calls == 1 && Field(left, "device.serial").Value == "SYNTH-AUTO-LEFT",
            "IP-only draft automatically obtains the real serial before building the complete candidate");
        var saved = draft.Build();
        Check(saved.Topology!.Readers[0].Parameters!["device.serial"] == "SYNTH-AUTO-LEFT" &&
            Wire.Encode(draft.Capabilities.Single(ParameterOwnership.IsRcs).Build()) == originalRcs && Field(right, "device.serial").Value == "",
            "resolved identity is in the saved candidate; RCS camera codes and the other camera are unchanged");
        Check(await model.PrepareCameraIdentitiesAsync() && calls == 1, "unchanged saved identity is not queried or replaced again");
        Field(left, "device.ip").Value = "192.0.2.11";
        Check(Field(left, "device.serial").Value == "", "changing IP clears the prior physical identity");
        var pending = new TaskCompletionSource<string>(); model.CameraSerialReader = (_, _) => pending.Task;
        var reading = model.PrepareCameraIdentitiesAsync(); Field(left, "device.ip").Value = "192.0.2.12"; pending.SetResult("STALE-SERIAL");
        Check(!await reading && Field(left, "device.serial").Value == "", "a late lookup for an older IP cannot overwrite the current draft");
        model.CameraSerialReader = (_, _) => throw new IOException("相机未连接，请检查网络。");
        Check(!await model.PrepareCameraIdentitiesAsync() && Field(left, "device.ip").Value == "192.0.2.12" && Wire.Encode(initial) == initialBytes,
            "lookup failure keeps the edited draft and the original configuration intact");
        Field(left, "device.ip").Value = "";
        Check(left.ConfigureLater && Field(left, "device.serial").Value == "", "clearing IP leaves this camera unconfigured");
        var readers = saved.Topology.Readers.Select(r => r with { Parameters = r.Parameters!.Where(p => p.Key != "reader.engine").ToDictionary() }).ToArray();
        var existing = saved with { Reader = readers[0], Topology = saved.Topology with { Readers = readers } };
        var existingDraft = new StartupConfigurationDraft(existing, catalog);
        var existingModel = existingDraft.CreateParameterEditor((_, _) => throw new InvalidOperationException());
        existingModel.CameraSerialReader = (_, _) => throw new InvalidOperationException("Must not rediscover a saved camera");
        Check(await existingModel.PrepareCameraIdentitiesAsync() && existingModel.Readers.All(r => Field(r, "reader.engine").Value == "zxing-cpp") &&
            Wire.Encode(existingDraft.Build()) == Wire.Encode(existing), "existing serials and omitted software-engine defaults remain byte-for-byte unchanged");
    }
}
