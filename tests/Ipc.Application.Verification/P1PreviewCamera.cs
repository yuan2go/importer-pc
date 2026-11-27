using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class P1PreviewCamera
{
    static readonly CameraRoundHarness.RoundPixels Empty = EmptyTrayImage();

    internal static IControlledObservationSource Create(DeviceBinding binding, string dataRoot)
    {
        var parameters = binding.Parameters!.ToDictionary();
        parameters["reader.engine"] = "mvid-native";
        binding = binding with { Parameters = parameters };
        var api = new CameraRoundHarness.RoundApi(binding) { AutomaticPreview = Empty };
        return new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, parameters), api,
            serial => new ResourceLease(Path.Combine(dataRoot, "preview-camera-leases"), [serial]));
    }

    static CameraRoundHarness.RoundPixels EmptyTrayImage()
    {
        const int width = 560, height = 320;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var bx = Math.Abs(x - width / 2);
                var by = Math.Abs(y - height / 2);
                var inside = bx <= 234 && by <= 119;
                var rim = bx >= 218 || by >= 102;
                pixels[y * width + x] = inside
                    ? (byte)(rim ? 155 + y / 20 : 105 + y / 10 + (x % 36 < 2 ? 8 : 0))
                    : (byte)(48 + y / 20);
            }
        return new(pixels, width, height);
    }
}
