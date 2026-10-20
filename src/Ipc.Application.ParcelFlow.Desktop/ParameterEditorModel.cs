using System.Collections.ObjectModel;
using System.ComponentModel;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParameterIssue(string Category, string Path, string Message);
// An editor over the existing complete draft, never a separately persisted configuration.
public sealed class ParameterEditorModel(ConfigurationPolicyDraft policy, string applicationId,
    CapabilityDraft[] readers, CapabilityDraft[] components, Func<BarcodeFilterPolicy, string[], Task<BarcodeFilterResult>> preview)
{
    public ConfigurationPolicyDraft Policy { get; } = policy;
    public string ApplicationId { get; } = applicationId;
    public CapabilityDraft[] Readers { get; } = readers;
    public CapabilityDraft[] Components { get; } = components;
    public ObservableCollection<ParameterIssue> Issues { get; } = [];
    public Func<BarcodeFilterPolicy, string[], Task<BarcodeFilterResult>> Preview { get; } = preview;
    public event EventHandler? ValidationChanged;
    public void Report(string message)
    {
        if (message.Contains("BarcodeFilter.Pattern", StringComparison.Ordinal)) Issues.Add(new("业务模块", "BarcodePattern", message));
        else if (message.Contains("StationCode", StringComparison.Ordinal) || message.Contains("station code", StringComparison.OrdinalIgnoreCase)) Issues.Add(new("系统", "StationCode", message));
        else
        {
            var matching = Readers.Concat(Components).SelectMany(d => d.Parameters.Select(p => (Draft: d, Field: p.Field))).Where(x => message.Contains(x.Field.Key, StringComparison.Ordinal)).ToArray();
            if (matching.Length == 0) Issues.Add(new("完整候选", "", message));
            else foreach (var item in matching) Issues.Add(new(item.Draft.Kind == "observation" ? "驱动" : "组件", item.Draft.InstanceId + "/" + item.Field.Key, message));
        }
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool ValidateFields()
    {
        Issues.Clear();
        void Issue(string category, string key, string message) => Issues.Add(new(category, key, message));
        void Number(string name, string value, int min, int max)
        { try { _ = ConfigurationPolicyDraft.Integer(value, name, min, max); } catch (InvalidOperationException ex) { Issue("系统", name, ex.Message); } }
        Number(nameof(Policy.QueueCapacity), Policy.QueueCapacity, 1, 4096); Number(nameof(Policy.TimeoutMs), Policy.TimeoutMs, 100, 30000);
        Number(nameof(Policy.MaximumOperations), Policy.MaximumOperations, 1, 100000); Number(nameof(Policy.ResidentHistory), Policy.ResidentHistory, 1, 10000);
        if (!decimal.TryParse(Policy.MinimumFreeMiB, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var reserve) || reserve < 1 || reserve > 1048576)
            Issue("系统", nameof(Policy.MinimumFreeMiB), "磁盘保留须为1～1048576 MiB");
        if (Policy.StationCode.Length > 256 || Policy.StationCode.Any(char.IsControl) || Policy.StationCode.Length > 0 && string.IsNullOrWhiteSpace(Policy.StationCode))
            Issue("系统", nameof(Policy.StationCode), "工位外部编码须为1～256个非控制字符");
        try { _ = Policy.BuildBackup(); } catch (InvalidOperationException ex) { Issue("系统", nameof(Policy.BackupDirectory), ex.Message); }
        foreach (var (draft, category) in Readers.Select(x => (x, "驱动")).Concat(Components.Where(x => x.Bound || x.Enabled).Select(x => (x, "组件"))))
        {
            if (draft.Provider is null) { Issue(category, draft.InstanceId, draft.ProviderIdentity); continue; }
            var values = draft.Parameters.Where(p => p.Value.Length > 0).ToDictionary(p => p.Field.Key, p => p.Value);
            foreach (var error in ExtensionSchema.Validate(draft.Provider.Parameters, values))
                Issue(category, draft.InstanceId + "/" + error.Split(':')[0], draft.Role + " · " + error);
        }
        ValidationChanged?.Invoke(this, EventArgs.Empty); return Issues.Count == 0;
    }
}
