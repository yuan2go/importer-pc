using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    void ReleaseIngressAcquisition(Observation o)
    {
        if (readers.GetValueOrDefault(o.DeviceId)?.Source is IOriginalFrameSource originals)
            originals.ReleaseOriginalFrame(o.Acquisition);
        if (readers.GetValueOrDefault(o.DeviceId)?.Source is IReaderImageCapture source)
            source.ReleaseAcquisition(o.Acquisition, "observation:" + o.Id);
    }
    void ReleaseOperationAcquisition(ParcelOperation op)
    {
        if (op.Acquisition is { } identity && readers.GetValueOrDefault(identity.DeviceId)?.Source is IReaderImageCapture source)
            source.ReleaseAcquisition(identity, "operation:" + op.Id);
    }
}
