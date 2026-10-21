using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    public Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy policy, string[] candidates)
    {
        // Offline, bounded, no configuration write or external/device operation.
        return Task.FromResult(BarcodeFiltering.Select(policy, candidates.ToArray()));
    }
    public async Task<ConfigurationSubmissionResult> InspectConfigurationSubmissionAsync(Guid owner, long expectedRevision, ParcelConfiguration candidate)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || owner != runtimeId || configuration is null || directory is null)
                return new("Unknown", "原运行实例不可核对；保留输入，不重新提交", expectedRevision, "");
            var normalized = factory.Normalize(Wire.Decode<ParcelConfiguration>(Wire.Encode(candidate)));
            var hash = Wire.Hash(Wire.Encode(normalized));
            var file = Path.Combine(directory, "configuration.json");
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration envelope quota exceeded");
            var actual = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
            if (Wire.Encode(actual) != Wire.Encode(configuration.Current))
                return new("Unknown", "磁盘账与运行所有者状态不同；保留原稿，进入受控启动核对", actual.Revision, hash);
            if (actual.Revision >= expectedRevision && actual.Pending is not null && Wire.Hash(Wire.Encode(actual.Pending)) == hash)
                return new("PendingRestart", "权威配置账已确认保存该候选；未应用，不重复提交", actual.Revision, hash);
            if (actual.Revision == expectedRevision && actual.Pending is null && Wire.Hash(Wire.Encode(actual.Effective)) == hash)
                return new("Unchanged", "候选与当前运行配置一致；没有创建待重启修订", actual.Revision, hash);
            return actual.Revision == expectedRevision
                ? new("NotSaved", "权威修订未改变；本次候选未保存，修正原因后可重试", actual.Revision, hash)
                : new("Conflict", "配置修订已变化，保留输入；重新读取比较后再决定", actual.Revision, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { return new("Unknown", "保存结果待核对：" + ex.Message, expectedRevision, ""); }
        finally { gate.Release(); }
    }
}
