using Ipc.Platform;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Drivers.Hikrobot.Probe")]
namespace Ipc.Drivers.Hikrobot.Mvs;

public sealed class MvsAcquisitionModule : IObservationExtension
{
    public IControlledObservationSource Create(ExtensionContext context) => new MvsAcquisition(context, new MvsApi());
}

// Station diagnostics remain observe-only and never install a decoder/sink.
internal sealed class MvsAcquisition : IControlledObservationSource
{
    readonly MvsCaptureSession capture;
    public string DeviceId => capture.DeviceId;
    public string Epoch => capture.Epoch;
    public string Phase => capture.Phase;
    public long Frames => capture.Frames;
    public string Failure => capture.Failure;
    public string ConfirmationSource => capture.ConfirmationSource;
    internal MvsAcquisition(ExtensionContext context, IMvsApi api, Func<string, IDisposable>? acquireLease = null, Action<string>? phaseChanged = null)
    {
        if (context.Parameters.GetValueOrDefault("entity.mode", "observe-only") != "observe-only")
            throw new InvalidDataException("DecoderUnavailable: acquisition cannot create parcel admissions; entity.mode must be observe-only");
        capture = new(context, api, acquireLease, phaseChanged);
    }
    public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive,
        Action<DeviceHealthConfirmation> health, CancellationToken token) => capture.StartControlledAsync(registeredEpoch, receive, health, token);
    public Task StopAsync(CancellationToken token) => capture.StopAsync(token);
    public ValueTask DisposeAsync() => capture.DisposeAsync();
    internal static void ValidateFrame(MvsFrame frame, int limit) => MvsCaptureSession.ValidateFrame(frame, limit);
}
