using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    CameraInspectionStore? parcelPictures;
    string? parcelPictureFailure;
    DateTimeOffset nextParcelPictureRetention;
    void InitializeParcelPictures()
    {
        if (Id != "sportmaster") return;
        // Main's business OriginalFrame archive is the only producer for daily
        // pictures. Open an existing 03 cache only for legacy read/retention.
        if (!Directory.Exists(Path.Combine(directory!, "parcel-pictures"))) return;
        try
        {
            parcelPictures = new(directory!, () => Effective.Retention?.MinimumFreeBytes ?? SystemParameterDefaults.MinimumFreeBytes,
                automatic: true, retentionDays: () => Effective.Retention?.HistoryDays ?? 7);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { parcelPictureFailure = "日常包裹留图不可用，原件保留：" + error.GetType().Name; }
    }
    void MaintainParcelPictures()
    {
        if (parcelPictures is not { } pictures || DateTimeOffset.UtcNow < nextParcelPictureRetention) return;
        nextParcelPictureRetention = DateTimeOffset.UtcNow.AddMinutes(1);
        pictures.ScheduleParcelRetention(Effective.Retention?.HistoryDays ?? 7);
    }
    public Task<ParcelPictureResult> ReadParcelPictureAsync(ParcelPictureQuery query, CancellationToken token) => OwnedRead(async owned =>
    {
        if ((query.OperationId is null) == (query.ObservationId is null) || query.OperationId?.Length > 256 || query.ObservationId?.Length > 256)
            throw new ArgumentException("必须提供一个准确原作业或当前观察身份，不能按条码猜配图像。");
        AcquisitionIdentity? acquisition = null; string? observationId = query.ObservationId; string? operationId = query.OperationId;
        string? operatorSession; string? expectedObservationHash = null; bool manual;
        CameraDebugRequestEvidence? debugPicture = null;
        OriginalFrameArchive? archived = null;
        await gate.WaitAsync(owned).ConfigureAwait(false);
        try
        {
            if (Id != "sportmaster" || !SessionValid || stopped) throw new InvalidOperationException("当前操作员会话或作业读取不可用。");
            operatorSession = currentOperator?.SessionId;
            if (operationId is not null)
            {
                var op = operations.GetValueOrDefault(operationId) ?? repository!.ReadOperation(operationId);
                if (op is null) return new ParcelPictureResult("Missing", "原作业不存在或已按保留策略清理；不能以同码的其他作业补图。", operationId, null, null);
                acquisition = op.Acquisition; expectedObservationHash = op.ObservationId; manual = op.OperatorInput is not null;
                if (op.DebugRequest is { } debug) { Management.Require(); debugPicture = debug; }
                archived = op.OriginalFrame;
                if (archived is not null && (archived.Acquisition != op.Acquisition || archived.RelativeFile != "images/original-frames/" + op.Id + ".png"))
                    throw new InvalidDataException("原业务留图身份/路径与原作业不符。");
            }
            else
            {
                var input = latestInputs.Values.SingleOrDefault(i => i.Observation.Id == observationId);
                if (input is null) return new ParcelPictureResult("Missing", "当前观察已变化，请从原作业记录打开对应图片。", null, observationId, null);
                var o = input.Observation; manual = o.OperatorInput is not null;
                if (o.InspectionSessionId is not null) return new ParcelPictureResult("Isolated", "本次是隔离调试输入；请在获授权的调试会话查看原始证据。", null, observationId, null);
                acquisition = manual ? null : new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt);
                operationId = input.OperationId;
                if (operationId is not null)
                {
                    var op = operations.GetValueOrDefault(operationId) ?? repository!.ReadOperation(operationId);
                    archived = op?.OriginalFrame;
                    if (archived is not null && (archived.Acquisition != acquisition || archived.RelativeFile != "images/original-frames/" + operationId + ".png"))
                        throw new InvalidDataException("本次观察与原业务留图身份不符。");
                }
            }
        }
        finally { gate.Release(); }
        if (debugPicture is { } originalDebug)
        {
            var authority = Management.Require();
            using var protectedRead = CancellationTokenSource.CreateLinkedTokenSource(owned, authority);
            var original = await Task.Run(() => InspectionStore.Image(originalDebug.FrameId), protectedRead.Token).ConfigureAwait(false);
            protectedRead.Token.ThrowIfCancellationRequested(); Management.Require();
            if (original is not null && (original.Metadata.DeviceId != originalDebug.DeviceId || original.Metadata.Epoch != originalDebug.Epoch ||
                original.Metadata.SourceEventId != originalDebug.SourceEventId || original.Metadata.OriginalSha256 != originalDebug.OriginalSha256 ||
                original.Metadata.IsolatedSessionId != originalDebug.SessionId)) throw new InvalidDataException("调试原图与原请求证据不一致，拒绝显示。");
            return new(original is null ? "Missing" : "Available", original is null ? "调试原图未保留或已清理；原测试请求和回执仍保留。" : "原隔离调试请求对应原帧；受当前管理授权保护。", operationId, original?.Metadata.ObservationId, original);
        }
        if (archived is not null && operationId is not null)
        {
            if (archived.State != "Saved") return new(archived.State, "原图留档状态：" + archived.State + "；" + (archived.Failure ?? "正在等待原帧后台保存，不重新拍照。"), operationId, observationId, null);
            var file = archived;
            var png = await Task.Run(() =>
            {
                if (file.Width <= 0 || file.Height <= 0 || (long)file.Width * file.Height > 33554432 || file.Length is not > 0 or > 41943040 ||
                    !ExtensionSchema.IsHash(file.FileSha256 ?? "") || !ExtensionSchema.IsHash(file.RawSha256 ?? "") ||
                    !ProductValidation.IsSafeRelativeFile(directory!, file.RelativeFile, out var path)) throw new InvalidDataException("原图留档格式/大小/哈希或路径无效。");
                RecoveryFiles.RequireOrdinaryAncestors(path);
                if (new FileInfo(path).Length != file.Length) throw new InvalidDataException("原图长度不符，保留原件。");
                var bytes = File.ReadAllBytes(path);
                if (bytes.LongLength != file.Length || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != file.FileSha256)
                    throw new InvalidDataException("原图文件完整性不符，拒绝展示。");
                return bytes;
            }, owned).ConfigureAwait(false);
            await gate.WaitAsync(owned).ConfigureAwait(false);
            try { if (!SessionValid || currentOperator?.SessionId != operatorSession || stopped) throw new InvalidOperationException("操作员会话已改变，图片未返回。"); }
            finally { gate.Release(); }
            return new ParcelPictureResult("Available", "原业务同帧PNG留档，文件身份与哈希已核对。", operationId, observationId, null)
                { Archived = new(operationId, file, png) };
        }
        if (manual) return new("NoAcquisition", "此条码来自键盘或PDA键盘输入，没有同帧相机采集证据，不使用实时图或其他包裹图片补配。", operationId, observationId, null);
        if (acquisition is null) return new("Missing", "原记录未包含可核对的相机采集身份，无法取得对应原图。", operationId, observationId, null);
        var identity = acquisition;
        var result = await Task.Run(() =>
        {
            if (parcelPictures is not { } store) return new ParcelPictureResult("Unavailable", parcelPictureFailure ?? "日常留图尚未可用。", operationId, observationId, null);
            var frame = observationId is null ? store.FindAcquisition(identity) : store.Find(observationId);
            if (frame is null) return new ParcelPictureResult("Missing", store.Fault ?? "对应原图尚未持久化、未留存或已按配额/保留期清理，可稍后刷新；业务记录仍保留。", operationId, observationId, null);
            if (frame.DeviceId != identity.DeviceId || frame.Epoch != identity.Epoch || frame.SourceEventId != identity.SourceEventId ||
                frame.CapturedAt != identity.ObservedAt || frame.IsolatedSessionId is not null ||
                expectedObservationHash is not null && Wire.Hash(frame.DeviceId + ":" + frame.Epoch + ":" + frame.SourceEventId) != expectedObservationHash)
                throw new InvalidDataException("原图与原包裹采集身份不一致，拒绝展示。");
            if (!frame.ImageAvailable) return new ParcelPictureResult("Evicted", frame.ImageState, operationId, frame.ObservationId, null);
            var image = store.Image(frame.Id);
            return new ParcelPictureResult(image is null ? "Evicted" : "Available", image is null ? "读取期间原图已按配额轮替，请保留原业务记录。" : "原包裹同帧图像，原始像素完整性已核对。",
                operationId, frame.ObservationId, image);
        }, owned).ConfigureAwait(false);
        await gate.WaitAsync(owned).ConfigureAwait(false);
        try { if (!SessionValid || currentOperator?.SessionId != operatorSession || stopped) throw new InvalidOperationException("操作员会话已改变，图片未返回。"); }
        finally { gate.Release(); }
        owned.ThrowIfCancellationRequested(); return result;
    }, token);
}
