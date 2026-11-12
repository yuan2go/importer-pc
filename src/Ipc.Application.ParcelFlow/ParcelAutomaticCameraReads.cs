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
        if (Id == "sportmaster" && Effective.Wcs?.InstanceId == value.InstanceId)
            Volatile.Write(ref automaticCameraRcsHealth, value);
    }
    bool AutomaticCameraReadsAllowed(string device)
    {
        if (Id != "sportmaster") return true;
        if (positions.Values.Any(p => p.ReaderDeviceId == device && !PositionSupplyEnabled(p.Id))) return false;
        var position = positions.Values.SingleOrDefault(p => p.ReaderDeviceId == device)?.Id;
        if (position is not null && UsesSmArrival(position) &&
            (!arrivalCaptures.TryGetValue(device, out var capture) || !capture.Ready || capture.Until <= DateTimeOffset.UtcNow || capture.Epoch != readers[device].Epoch)) return false;
        var health = Volatile.Read(ref automaticCameraRcsHealth);
        var now = DateTimeOffset.UtcNow;
        return health is { Ready: true, RemoteConfirmation: RemoteConnectionConfirmation.ResponseReceived } &&
            health.ConfirmedAt <= now && health.ValidUntil > now && health.RemoteConfirmedAt <= now && health.RemoteValidUntil > now;
    }
    InspectionRegistration? AutomaticReadRegistration(InspectionRegistration? registration)
    {
        if (Id != "sportmaster") return registration;
        if (registration is null)
            return new(automaticCameraReadScope, false, _ => true, false) { DecodeAllowed = AutomaticCameraReadsAllowed };
        // Explicit diagnostic rounds remain available without RCS. Their own
        // selected-frame predicate and explicit send guard remain authoritative.
        if (registration.CutoverKey is not null) return registration;
        var previous = registration.DecodeAllowed;
        return registration with { DecodeAllowed = device => AutomaticCameraReadsAllowed(device) && previous?.Invoke(device) != false };
    }
}
