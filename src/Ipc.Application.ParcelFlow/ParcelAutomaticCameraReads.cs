using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly string automaticCameraReadScope = Wire.Id();
    CapabilityHealth? automaticCameraRcsHealth;
    void RecordCapabilityHealth(CapabilityHealth value)
    {
        capabilityHealth[value.InstanceId] = value;
        if (definition.RequiresArrivalHeartbeatForAutomaticRead && Effective.Wcs?.InstanceId == value.InstanceId)
            Volatile.Write(ref automaticCameraRcsHealth, value);
    }
    bool AutomaticCameraReadsAllowed(string device) => AutomaticCameraReadBlock(device) is null;
    string? AutomaticCameraReadBlock(string device)
    {
        if (!definition.RequiresArrivalHeartbeatForAutomaticRead) return null;
        if (positions.Values.Any(p => p.ReaderDeviceId == device && !PositionSupplyEnabled(p.Id))) return "PositionSupplyDisabled";
        var health = Volatile.Read(ref automaticCameraRcsHealth);
        var now = DateTimeOffset.UtcNow;
        if (!(health is { Ready: true, RemoteConfirmation: RemoteConnectionConfirmation.ResponseReceived } &&
            health.ConfirmedAt <= now && health.ValidUntil > now && health.RemoteConfirmedAt <= now && health.RemoteValidUntil > now))
            return "RcsRemoteConfirmationUnavailable";
        var position = positions.Values.SingleOrDefault(p => p.ReaderDeviceId == device)?.Id;
        if (position is not null && UsesSmArrival(position) &&
            (!arrivalCaptures.TryGetValue(device, out var capture) || !capture.Ready || capture.Until <= now || capture.Epoch != readers[device].Epoch))
            return "CameraArrivalBoundaryUnconfirmed";
        return null;
    }
    InspectionRegistration? AutomaticReadRegistration(InspectionRegistration? registration)
    {
        if (!definition.RequiresArrivalHeartbeatForAutomaticRead) return registration;
        if (registration is null)
            return new(automaticCameraReadScope, false, _ => true, false) { DecodeAllowed = AutomaticCameraReadsAllowed,
                MinimumDecodeIntervalMs = device => smReadIntervals.GetValueOrDefault(device) };
        // Explicit diagnostic rounds remain available without RCS. Their own
        // selected-frame predicate and explicit send guard remain authoritative.
        if (registration.CutoverKey is not null) return registration;
        var previous = registration.DecodeAllowed;
        return registration with { DecodeAllowed = device => AutomaticCameraReadsAllowed(device) && previous?.Invoke(device) != false,
            MinimumDecodeIntervalMs = device => smReadIntervals.GetValueOrDefault(device) };
    }
}
