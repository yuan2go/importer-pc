using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public enum ParcelStage { Capture, Upload, Wcs, Plc }
public sealed partial class ParcelRuntime
{
    static StageFact? GetStage(ParcelOperation op, ParcelStage kind) => kind switch
    { ParcelStage.Capture => op.Capture, ParcelStage.Upload => op.Upload, ParcelStage.Wcs => op.Wcs, _ => op.Plc };
    static ParcelOperation SetStage(ParcelOperation op, ParcelStage kind, StageFact fact) => kind switch
    { ParcelStage.Capture => op with { Capture = fact }, ParcelStage.Upload => op with { Upload = fact }, ParcelStage.Wcs => op with { Wcs = fact }, _ => op with { Plc = fact } };
    static CapabilityBinding? BindingFor(ParcelConfiguration c, ParcelStage kind, string? position) => kind switch
    { ParcelStage.Capture => ParcelBindings.Camera(c, position), ParcelStage.Upload => c.Images?.Uploader, ParcelStage.Wcs => c.Wcs, _ => c.Plc };
    ICapabilityPort PortFor(ParcelOperation op, ParcelStage kind) => Capability(GetStage(op, kind)!.Request.Binding.InstanceId);
    static ParcelState UnknownState(ParcelStage kind) => kind switch
    { ParcelStage.Wcs => ParcelState.WcsUnknown, ParcelStage.Plc => ParcelState.PlcUnknown, _ => ParcelState.ImageUnknown };
    static ParcelStage ParseStage(string stage) => stage switch
    { "image-capture" => ParcelStage.Capture, "image-upload" => ParcelStage.Upload, "wcs-slot" => ParcelStage.Wcs, "plc-submit" => ParcelStage.Plc, _ => throw new InvalidDataException("Unknown original stage") };
    static bool IsDebugRequest(ParcelOperation op) => op.DebugRequest is not null || op.OperatorDebugRequest is not null;
    bool CanDispatch(ParcelOperation op) => IsDebugRequest(op) ? explicitDebugSends.Contains(op.Id) && DebugRequestReady(op) : CanDispatchNormally(op);
    bool CanDispatchNormally(ParcelOperation op) => Volatile.Read(ref accepting) == 1 && !ParcelConnectionSetup.BlocksWork(Effective) && (IsManualParcel(op) || !ParcelConnectionSetup.IsDeferred(Effective, positions[op.PositionId ?? "main"].ReaderDeviceId)) && (IsManualParcel(op) ? ManualPositionMaintenanceClear(op.PositionId!) : PositionMaintenanceClear(op.PositionId ?? "main")) && !op.RecoveryRequired && !HasIdentityConflict(op.PositionId ?? "main") && !WeightExpired(op) && factory.AdmissionFailure(Effective) is null && !stopping.IsCancellationRequested && failure is null && (overload == 0 || !overloadedPositions.ContainsKey(op.PositionId ?? "main")) && !repository!.Paused && !CameraBusinessFenced &&
        SessionValid && (Effective.Login is null || currentOperator?.Subject == op.Operator?.Subject && currentOperator is not null) &&
        !repository.PendingReceipts().Any(x => x.Operation == op.Id && !(op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached && x.Stage == "image-upload")) &&
        Enum.GetValues<ParcelStage>().Where(kind => !(kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached))
            .All(kind => GetStage(op, kind)?.State is not (StageState.Unknown or StageState.Conflict or StageState.Dispatching)) &&
        op.State is not (ParcelState.WeightTimedOut or ParcelState.Abandoned or ParcelState.ImageRejected);
    bool PredecessorsAccepted(ParcelOperation op, ParcelStage kind) => kind switch
    {
        ParcelStage.Capture => true,
        ParcelStage.Upload => op.Capture?.State == StageState.Accepted,
        _ => (op.FrozenConfiguration.Images is null || op.Capture?.State == StageState.Accepted && (op.FrozenConfiguration.Images.Uploader is null || op.FrozenConfiguration.Images.Strategy == UploadStrategy.Attached || op.Upload?.State == StageState.Accepted)) && definition.PredecessorsAccepted(op, kind)
    };
    async Task<ParcelOperation> CaptureOriginalAsync(ParcelOperation op)
    {
        if (op.Capture is null)
        {
            op = op with { Capture = new(Prepare(op, "image-capture", ParcelBindings.Camera(op.FrozenConfiguration, op.PositionId)!, null, null), StageState.Prepared, 0, []) };
            Save(op, "CapturePrepared", "T1: capture bound to original observation/admission before any weight wait");
        }
        if (op.Capture is { State: StageState.Prepared, Attempts: 0 }) op = await ExecuteStageAsync(op, ParcelStage.Capture, false).ConfigureAwait(false);
        if (op.FrozenConfiguration.Images!.Uploader is not null && op.Capture?.State == StageState.Accepted && op.Upload is null)
        {
            var prepared = PrepareUploadIfReady(op);
            if (prepared != op) { op = prepared; Save(op, "UploadPrepared", "Required upload inputs are ready; freeze original upload intent"); }
        }
        if (op.FrozenConfiguration.Images!.Strategy == UploadStrategy.Attached && op.Capture?.State == StageState.Accepted) ScheduleAttached(op);
        return operations[op.Id];
    }
    async Task<ParcelOperation> AdvanceImagesAsync(ParcelOperation op)
    {
        op = await CaptureOriginalAsync(op).ConfigureAwait(false);
        if (op.Capture?.State != StageState.Accepted) return op;
        if (WeightReady(op) && op.FrozenConfiguration.Images!.Uploader is not null && op.FrozenConfiguration.Images!.Strategy == UploadStrategy.BeforeMain && op.Upload is { State: StageState.Prepared, Attempts: 0 } && CanDispatch(op))
            op = await ExecuteStageAsync(op, ParcelStage.Upload, false).ConfigureAwait(false);
        return operations[op.Id];
    }
    void ScheduleAttached(ParcelOperation op)
    {
        if (op.Upload is not { State: StageState.Prepared, Attempts: 0 } || !attachedRunning.Add(op.Id)) return;
        auxiliary.Add(Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var latest = operations[op.Id];
                if (!stopping.IsCancellationRequested && !repository!.Paused && !CameraBusinessFenced && latest.Upload is { State: StageState.Prepared, Attempts: 0 })
                    await ExecuteStageAsync(latest, ParcelStage.Upload, false).ConfigureAwait(false);
            }
            catch (Exception ex) { failure = "Attached upload persistence failure: " + ex.Message; Interlocked.Exchange(ref accepting, 0); }
            finally { attachedRunning.Remove(op.Id); gate.Release(); }
        }));
    }
    byte[] ReadImage(ParcelOperation op, ParcelImage image) => ReadOriginalImage(directory!, op, image);
    internal static byte[] ReadOriginalImage(string directory, ParcelOperation op, ParcelImage image)
    {
        var path = RequireOriginalImagePath(directory, op, image);
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != image.Sha256) throw new InvalidDataException("Original image bytes changed");
        return bytes;
    }
    internal static void ValidateBackupImage(string directory, ParcelOperation op, ParcelImage image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var path = RequireOriginalImagePath(directory, op, image);
        if (Wire.FileHash(path, token) != image.Sha256 || new FileInfo(path).Length != image.Length) throw new InvalidDataException("Original image bytes changed");
    }
    static string RequireOriginalImagePath(string directory, ParcelOperation op, ParcelImage image)
    {
        if (image.AdmissionId != op.AdmissionId || image.CaptureRequestId != op.Capture?.Request.Id || !ValidMediaType(image.MediaType) || !ExtensionSchema.IsHash(image.Sha256) ||
            image.Length is < 1 || image.Length > op.FrozenConfiguration.Images!.MaximumBytes ||
            !image.RelativeFile.StartsWith("images/", StringComparison.Ordinal) || !ProductValidation.IsSafeRelativeFile(directory!, image.RelativeFile, out var path) ||
            new FileInfo(path).Length != image.Length) throw new InvalidDataException("Image identity, path or quota differs from the original capture");
        return path;
    }
    bool ValidImageReply(ParcelOperation op, ParcelStage kind, ParcelImage? image)
    {
        if (image is null || image.Acquisition != op.Acquisition) return false;
        try { _ = ReadImage(op, image); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
        return kind == ParcelStage.Capture ? image.UploadedUri is null : op.Image is { } captured &&
            image with { UploadedUri = null } == captured with { UploadedUri = null } &&
            Uri.TryCreate(image.UploadedUri, UriKind.Absolute, out var uri) && uri.Scheme is "reference" or "https" or "http" && string.IsNullOrEmpty(uri.UserInfo);
    }
    ParcelOperation ApplyImageReply(ParcelOperation op, ParcelStage kind, StageReply reply)
    {
        if (reply.BusinessAccepted != true) return op.FrozenConfiguration.Images!.Strategy == UploadStrategy.Attached && kind == ParcelStage.Upload ? op : op with { State = ParcelState.ImageRejected, Reason = reply.Evidence };
        op = op with { Image = reply.Image };
        if (op.State is ParcelState.ImageUnknown or ParcelState.CapturingImage or ParcelState.UploadingImage)
            op = op with { State = op.WeightPolicy.Source == WeightSource.Measured && op.Weight is null ? ParcelState.WaitingWeight : ParcelState.Prepared, Reason = null };
        if (kind == ParcelStage.Capture && op.Upload is null && op.FrozenConfiguration.Images!.Uploader is not null) op = PrepareUploadIfReady(op);
        return op;
    }
    ParcelOperation PrepareUploadIfReady(ParcelOperation op)
    {
        var binding = op.FrozenConfiguration.Images!.Uploader;
        if (binding is null) return op;
        if (factory.Requirements(binding).MeasuredWeight && !WeightReady(op)) return op;
        return op with { Upload = new(Prepare(op, "image-upload", binding, null, null), StageState.Prepared, 0, []) };
    }
    static bool ValidMediaType(string type) => type.Length is > 0 and <= 128 && type.Contains('/') && type.All(x => char.IsAsciiLetterOrDigit(x) || x is '/' or '-' or '+' or '.');
    bool ImageCapacityAvailable()
    {
        if (Effective.Images is not { } policy) return true;
        var pending = operations.Values.Where(x => x.FrozenConfiguration.Images is not null && !x.ImageReleased).ToArray();
        return pending.Length < policy.MaximumPending && pending.Sum(x => (long)(x.Image?.Length ?? x.FrozenConfiguration.Images!.MaximumBytes)) + policy.MaximumBytes <= policy.TotalBytes;
    }
    void RecoverImageCleanup()
    {
        ReleaseCompletedImages();
        foreach (var op in operations.Values.Where(x => x.ImageReleased && x.Image is not null)) DeleteImage(op);
    }
    void ReleaseCompletedImages()
    {
        foreach (var original in operations.Values.Where(x => x.FrozenConfiguration.Images is not null && !x.ImageReleased &&
            (x.FrozenConfiguration.Images!.Uploader is null && MainTerminated(x) || x.Upload?.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled ||
             x.Image is null && (x.Capture?.State is StageState.Rejected or StageState.Cancelled || x.Capture is null && x.State == ParcelState.Abandoned))).ToArray())
        {
            if (original.Capture?.State is StageState.Unknown or StageState.Conflict or StageState.Dispatching ||
                original.Upload?.State is StageState.Unknown or StageState.Conflict or StageState.Dispatching ||
                activeStages.Any(x => x.StartsWith(original.Id + ":", StringComparison.Ordinal)) || repository!.PendingReceipts().Any(x => x.Operation == original.Id)) continue;
            var op = original with { ImageReleased = true }; Save(op, "ImageReleased", "No remaining image responsibility; durable release precedes temporary byte deletion"); Boundary?.Invoke("AfterImageReleaseDurable");
            ReleaseOperationAcquisition(op);
            if (op.Image is not null) DeleteImage(op);
        }
    }
    void DeleteImage(ParcelOperation op)
    {
        if (!ProductValidation.IsSafeRelativeFile(directory!, op.Image!.RelativeFile, out var path) || !op.Image.RelativeFile.StartsWith("images/", StringComparison.Ordinal)) throw new InvalidDataException("Unsafe released image path");
        if (File.Exists(path)) File.Delete(path);
    }
}
