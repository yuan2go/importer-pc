using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    CameraOriginalImage? RetainInputPicture(Observation o, ObservationSession reader)
    {
        CameraOriginalImage? image = null;
        if (reader.Source is IOriginalFrameSource source)
        {
            try
            {
                using var original = source.RetainOriginalFrame(o.Acquisition);
                var pixels = original.Pixels.ToArray();
                _ = uint.TryParse(o.SourceEventId.Split('.').Last(), out var frame);
                image = new(new(o.Id, "", o.DeviceId, o.Epoch, frame, o.ObservedAt, original.Width, original.Height,
                    o.Symbols ?? [], 0, o.Id, o.SourceEventId, null, Convert.ToHexString(SHA256.HashData(pixels)), true, "Available"), pixels);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            { Diagnostic("原图留档", "当前扫码原图不可用", "警告", "Observation=" + o.Id + "; " + error); }
        }
        return image;
    }

}
