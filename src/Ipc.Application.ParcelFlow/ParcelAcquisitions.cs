using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    void ReleaseIngressAcquisition(Observation o)
    {
        if (readers.GetValueOrDefault(o.DeviceId)?.Source is IOriginalFrameSource originals)
            originals.ReleaseOriginalFrame(new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt));
        if (readers.GetValueOrDefault(o.DeviceId)?.Source is IReaderImageCapture source)
            source.ReleaseAcquisition(new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt), "observation:" + o.Id);
    }
    void ReleaseOperationAcquisition(ParcelOperation op)
    {
        if (op.Acquisition is { } identity && readers.GetValueOrDefault(identity.DeviceId)?.Source is IReaderImageCapture source)
            source.ReleaseAcquisition(identity, "operation:" + op.Id);
    }
}
