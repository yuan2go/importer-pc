using System.Reflection;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal object[] SupportEvents(ProductSupportRequest request)
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT at,event FROM parcel_audit WHERE julianday(at)>=julianday($from) AND julianday(at)<julianday($to) ORDER BY seq DESC LIMIT 256";
        c.Parameters.AddWithValue("$from", request.From.ToString("O")); c.Parameters.AddWithValue("$to", request.ToExclusive.ToString("O"));
        List<object> rows = []; using var r = c.ExecuteReader();
        while (r.Read()) { var code = r.GetString(1); rows.Add(new { At = r.GetString(0), EventCode = Regex.IsMatch(code, "^[A-Za-z][A-Za-z0-9]{0,63}$") ? code : "Other" }); }
        return rows.ToArray();
    }
}
public sealed partial class ParcelRuntime : IParcelSupportApplication
{
    readonly Dictionary<string, ProductSupportPreview> supportPreviews = [];
    public async Task<ProductSupportPreview> PreviewSupportAsync(ProductSupportRequest request)
    {
        if (request.From >= request.ToExclusive || request.ToExclusive - request.From > TimeSpan.FromDays(7)) throw new InvalidDataException("支持时间范围须为0～7天，最多包含256条脱敏事件代码");
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var op = request.OperationId is null ? null : repository!.ReadOperation(request.OperationId) ?? throw new InvalidDataException("未找到所选原作业");
            object? Request(StageFact? stage) => stage is null ? null : new { stage.Request.Id, stage.State, stage.Attempts, stage.Request.ConfigurationHash, stage.Request.PayloadHash,
                TargetSha256 = Wire.Hash(stage.Request.Binding.Target), ContractSha256 = Wire.Hash(stage.Request.Binding.Contract), Implementation = stage.Request.ImplementationHash };
            var at = DateTimeOffset.UtcNow;
            var json = Wire.Encode(new { Format = "ImporterPc.Support.v1", Application = Id, At = at,
                SourceIdentity = definition.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                BusinessVersion = BusinessCompatibility.VersionOf(definition.GetType().Assembly), ApplicationContractSha256 = Wire.FileHash(typeof(IParcelApplication).Assembly.Location),
                DatabaseVersion = repository!.StorageVersion, ConfigurationSchema = Effective.SchemaVersion, Mode = Effective.Mode,
                StationSha256 = Wire.Hash(Effective.StationId), EffectiveConfigurationSha256 = Wire.Hash(Wire.Encode(configuration!.Current.Effective)), ConfigurationRevision = configuration.Current.Revision,
                ConfigurationState = configuration.Current.Status, ConfirmationRequired = configuration.Current.Confirmation?.Required,
                Packages = Effective.Extensions?.Packages ?? [], PendingPackages = configuration.Current.Pending?.Extensions?.Packages ?? [],
                Resources = capabilityHealth.Values.Select(h => new { h.InstanceId, h.Ready, h.ConfirmedAt, h.ValidUntil }).ToArray(),
                Capacity = Capacity(), StateFlags = new { Paused = repository.Paused, HasFailure = failure is not null, HasOverload = overload != 0, Conflicts = identityConflicts.Count, ReceiptResponsibilities = repository.PendingReceipts().Length },
                Selection = new { request.From, request.ToExclusive, request.OperationId }, Events = repository.SupportEvents(request),
                Operation = op is null ? null : new { op.Id, op.State, op.ObservedAt, BarcodeSha256 = Wire.Hash(op.ParcelCode), op.ConfigurationHash, Wcs = Request(op.Wcs), Plc = Request(op.Plc), Capture = Request(op.Capture), Upload = Request(op.Upload) },
                Qualification = factory.AdmissionFailure(Effective) is null ? "Current deployment admission check passed; not site approval" : "NotReady" });
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 262144) throw new InvalidDataException("支持摘要超过256KiB预算");
            foreach (var id in supportPreviews.Where(x => at - x.Value.At > TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray()) supportPreviews.Remove(id);
            if (supportPreviews.Count == 8) supportPreviews.Remove(supportPreviews.MinBy(x => x.Value.At).Key);
            var preview = new ProductSupportPreview(Wire.Id(), Wire.Hash(json), at,
                ["精确源码/业务/公共契约/DB/config/包身份", "选定时间内最多256条事件代码（不含详情）", "健康时间、容量和责任计数", "所选原请求关联摘要（如已选择）"],
                ["密码/令牌/私钥及完整参数", "数据库、条码明文、目标地址、原业务报文和原件", "任意磁盘日志及自动上传"], json);
            supportPreviews.Add(preview.Id, preview); return preview;
        }
        finally { gate.Release(); }
    }
    public async Task ExportSupportAsync(string previewId, string destination, CancellationToken token)
    {
        ProductSupportPreview preview;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            preview = supportPreviews.GetValueOrDefault(previewId) ?? throw new InvalidOperationException("先预览本次支持内容");
            if (DateTimeOffset.UtcNow - preview.At > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("支持预览超过5分钟；重新预览");
        }
        finally { gate.Release(); }
        destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
        var relative = Path.GetRelativePath(directory!, destination);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("支持文件不能写入业务目录");
        var temporary = destination + "." + Wire.Id() + ".partial";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            { await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(preview.Json), token); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
