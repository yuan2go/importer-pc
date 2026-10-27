using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Explicit composition seam. The shipping module supplies no decoder. A future
// qualified adapter constructs its decoder through the existing SDK boundary;
// neither parameters nor this factory discovers or loads arbitrary vendor code.
public static class MvsDecoding
{
    public static IControlledObservationSource Create(ExtensionContext context, Func<IFrameDecoder> decoderFactory)
        => BundledMvs.CreateWithDecoder(context, decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory)));
}

internal sealed partial class MvsAcquisition
{
    readonly Func<IFrameDecoder>? decoderFactory;
    readonly MvsDecodeOptions decodeOptions;
    readonly DecodeMetrics metrics = new();
    readonly object healthPublication = new();
    DecodePipeline? pipeline;
    Task? decoderHealthReporter;
    TaskCompletionSource<bool>? decoderFailure;
    bool acquisitionHealthy, triggerVerified, fenced, stopRequested;
    public DecodeStatistics Statistics => metrics.Snapshot(pipeline?.BuffersInUse ?? 0);
    public DecoderState DecoderStatus => pipeline?.State ?? DecoderState.Unavailable;
    bool BusinessReady => !fenced && !stopRequested && !disposeRequested && acquisitionHealthy && triggerVerified && decodeOptions.Selection is not null &&
        decodeOptions.TriggerMode && pipeline?.State == DecoderState.Ready;
    string DecodeConfirmation => $"decoder={DecoderStatus}; acquisitionHealthy={acquisitionHealthy.ToString().ToLowerInvariant()}; triggerVerified={triggerVerified.ToString().ToLowerInvariant()}; " +
        $"selectionValid={(decodeOptions.Selection is not null).ToString().ToLowerInvariant()}; entityMode={(decodeOptions.TriggerMode ? "trigger" : "observe-only")}; businessReady={BusinessReady.ToString().ToLowerInvariant()}; " +
        $"rule={decodeOptions.Selection?.Identity ?? "unavailable"}; statistics={Wire.Encode(Statistics)}; decodeFailure={pipeline?.Failure}";
    void DecoderChanged()
    {
        if (pipeline?.State is not (DecoderState.Degraded or DecoderState.Faulted)) return;
        lock (gate) { fenced = true; acquisitionHealthy = false; }
        decoderFailure?.TrySetResult(true);
    }
    async Task ReportDecoderFault(Task<bool> signal)
    {
        if (!await signal.ConfigureAwait(false)) return;
        try { Publish(DeviceHealth.Faulted); }
        catch (Exception ex) { lock (gate) failure = "DecoderHealthReceiver:" + ex.GetType().Name; }
    }
    void VerifyTrigger()
    {
        if (!decodeOptions.TriggerMode) return;
        if (api.EnumValue("TriggerMode") != 1 || api.EnumValue("TriggerSource") != decodeOptions.TriggerSource ||
            api.EnumValue("TriggerSelector") != decodeOptions.TriggerSelector)
            throw new InvalidDataException("Configured trigger mode/source/selector differs from camera; no settings written");
        lock (gate) triggerVerified = true;
    }
}
