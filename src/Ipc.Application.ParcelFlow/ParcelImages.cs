using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

enum ParcelStage { Capture, Upload, Wcs, Plc }
public sealed partial class ParcelRuntime
{
    static StageFact? GetStage(ParcelOperation op, ParcelStage kind) => kind switch
    { ParcelStage.Capture => op.Capture, ParcelStage.Upload => op.Upload, ParcelStage.Wcs => op.Wcs, _ => op.Plc };
    static ParcelOperation SetStage(ParcelOperation op, ParcelStage kind, StageFact fact) => kind switch
    { ParcelStage.Capture => op with { Capture = fact }, ParcelStage.Upload => op with { Upload = fact }, ParcelStage.Wcs => op with { Wcs = fact }, _ => op with { Plc = fact } };
    static CapabilityBinding? BindingFor(ParcelConfiguration c, ParcelStage kind) => kind switch
    { ParcelStage.Capture => c.Images?.Camera, ParcelStage.Upload => c.Images?.Uploader, ParcelStage.Wcs => c.Wcs, _ => c.Plc };
    ICapabilityPort PortFor(ParcelStage kind) => kind switch
    { ParcelStage.Capture => ports!.Camera!, ParcelStage.Upload => ports!.Uploader!, ParcelStage.Wcs => ports!.Wcs!, _ => ports!.Plc! };
    static ParcelState UnknownState(ParcelStage kind) => kind switch
    { ParcelStage.Wcs => ParcelState.WcsUnknown, ParcelStage.Plc => ParcelState.PlcUnknown, _ => ParcelState.ImageUnknown };
    static ParcelStage ParseStage(string stage) => stage switch
    { "image-capture" => ParcelStage.Capture, "image-upload" => ParcelStage.Upload, "wcs-slot" => ParcelStage.Wcs, "plc-submit" => ParcelStage.Plc, _ => throw new InvalidDataException("Unknown original stage") };
    bool CanDispatch(ParcelOperation op) => !stopping.IsCancellationRequested && failure is null && overload == 0 && !repository!.Paused &&
        (Effective.Login is null || currentOperator?.Subject == op.Operator?.Subject && currentOperator is not null) &&
        (op.WeightPolicy.Source == WeightSource.Disabled || op.Weight is not null) &&
        !repository.PendingReceipts().Any(x => x.Operation == op.Id) &&
        Enum.GetValues<ParcelStage>().All(kind => GetStage(op, kind)?.State is not (StageState.Unknown or StageState.Conflict or StageState.Dispatching));
    bool PredecessorsAccepted(ParcelOperation op, ParcelStage kind) => kind switch
    {
        ParcelStage.Capture => true,
        ParcelStage.Upload => op.Capture?.State == StageState.Accepted,
        ParcelStage.Wcs => op.FrozenConfiguration.Images is null || op.Upload?.State == StageState.Accepted,
        _ => (op.FrozenConfiguration.Images is null || op.Upload?.State == StageState.Accepted) && (!definition.RequiresWcs || op.Wcs?.State == StageState.Accepted)
    };
    async Task<ParcelOperation> AdvanceImagesAsync(ParcelOperation op)
    {
        if (op.Capture is null)
        {
            op = op with { Capture = new(Prepare(op, "image-capture", op.FrozenConfiguration.Images!.Camera, null, null), StageState.Prepared, 0, []), State = ParcelState.CapturingImage };
            Save(op, "CapturePrepared", "T1: image capture belongs to this admitted parcel");
        }
        if (op.Capture is { State: StageState.Prepared, Attempts: 0 }) op = await ExecuteStageAsync(op, ParcelStage.Capture, false).ConfigureAwait(false);
        if (op.Capture?.State != StageState.Accepted) return op;
        if (op.Upload is { State: StageState.Prepared, Attempts: 0 } && !stopping.IsCancellationRequested && !repository!.Paused && overload == 0)
            op = await ExecuteStageAsync(op, ParcelStage.Upload, false).ConfigureAwait(false);
        return op;
    }
    byte[] ReadImage(ParcelOperation op, ParcelImage image)
    {
        if (image.AdmissionId != op.AdmissionId || image.CaptureRequestId != op.Capture?.Request.Id || image.MediaType != "image/png" || Path.GetFileName(image.RelativeFile) != image.CaptureRequestId + ".png" ||
            image.Length is < 1 || image.Length > op.FrozenConfiguration.Images!.MaximumBytes ||
            !image.RelativeFile.StartsWith("images/", StringComparison.Ordinal) || !ProductValidation.IsSafeRelativeFile(directory!, image.RelativeFile, out var path) ||
            new FileInfo(path).Length != image.Length) throw new InvalidDataException("Image identity, path or quota differs from the original capture");
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != image.Sha256) throw new InvalidDataException("Original image bytes changed");
        return bytes;
    }
    bool ValidImageReply(ParcelOperation op, ParcelStage kind, ParcelImage? image)
    {
        if (image is null) return false;
        try { _ = ReadImage(op, image); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
        return kind == ParcelStage.Capture ? image.UploadedUri is null : op.Image is { } captured &&
            image with { UploadedUri = null } == captured with { UploadedUri = null } &&
            Uri.TryCreate(image.UploadedUri, UriKind.Absolute, out var uri) && uri.Scheme == "reference";
    }
    ParcelOperation ApplyImageReply(ParcelOperation op, ParcelStage kind, StageReply reply)
    {
        if (reply.BusinessAccepted != true) return op with { State = ParcelState.ImageRejected, Reason = reply.Evidence };
        op = op with { Image = reply.Image, State = ParcelState.Prepared, Reason = null };
        if (kind == ParcelStage.Capture && op.Upload is null)
            op = op with { Upload = new(Prepare(op, "image-upload", op.FrozenConfiguration.Images!.Uploader, null, null), StageState.Prepared, 0, []), State = ParcelState.UploadingImage };
        return op;
    }
}
