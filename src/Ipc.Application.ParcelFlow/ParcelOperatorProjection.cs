using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    // Public operation/history projections retain business facts, never the editable configuration,
    // original configuration envelopes, provider credentials or raw request payloads for operators.
    // Stored originals are untouched; authenticated administrators receive those exact originals.
    static CapabilityBinding? OperatorBinding(CapabilityBinding? b) => b is null ? null : b with { Target = "", Parameters = [] };
    static DeviceBinding OperatorReader(DeviceBinding r) => r with { Parameters = null };
    static ParcelConfiguration WorkstationConfiguration(ParcelConfiguration original)
    {
        var masked = OperatorConfiguration(original);
        if (original.ApplicationId != "sportmaster") return masked;
        Dictionary<string, string>? Fields(Dictionary<string, string>? parameters) => parameters?
            .Where(p => SmParameterOwnership.FieldParameter(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        DeviceBinding Reader(DeviceBinding reader) => reader with { Parameters = Fields(reader.Parameters) };
        var endpoint = original.Wcs is { } binding && Uri.TryCreate(binding.Target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority) : "";
        return masked with
        {
            Reader = Reader(original.Reader),
            Topology = original.Topology is { } topology ? topology with { Readers = topology.Readers.Select(Reader).ToArray() } : null,
            Wcs = masked.Wcs is { } rcs ? rcs with { Target = endpoint, Parameters = Fields(original.Wcs!.Parameters)! } : null
        };
    }
    static ParcelConfiguration OperatorConfiguration(ParcelConfiguration c) => c with
    {
        Reader = OperatorReader(c.Reader),
        Scale = OperatorBinding(c.Scale),
        Wcs = OperatorBinding(c.Wcs),
        Plc = OperatorBinding(c.Plc),
        Login = OperatorBinding(c.Login),
        MaintenanceLogin = null,
        CapabilityInstances = null,
        Extensions = null,
        BarcodeFilter = null,
        BackupSchedule = null,
        Topology = c.Topology is null ? null : c.Topology with { Readers = c.Topology.Readers.Select(OperatorReader).ToArray() },
        Images = c.Images is null ? null : c.Images with { Camera = OperatorBinding(c.Images.Camera)!, Uploader = OperatorBinding(c.Images.Uploader)! }
    };
    static StageFact? OperatorStage(StageFact? s) => s is null ? null : s with
    {
        Request = s.Request with { Binding = OperatorBinding(s.Request.Binding)!, Payload = "", IntegrationRequest = null },
        Replies = s.Replies.Select(r => r with { HttpResponse = null }).ToArray()
    };
    static ParcelOperation OperatorOperation(ParcelOperation op) => op with
    {
        FrozenConfiguration = OperatorConfiguration(op.FrozenConfiguration),
        Wcs = OperatorStage(op.Wcs),
        Plc = OperatorStage(op.Plc),
        Capture = OperatorStage(op.Capture),
        Upload = OperatorStage(op.Upload),
        BarcodeSelection = null,
        ReaderBinding = op.ReaderBinding is null ? null : OperatorReader(op.ReaderBinding)
    };
    ParcelHistoryPage OperatorHistory(ParcelHistoryPage page) => Management.Status().Authorized ? page : page with
    { Items = page.Items.Select(item => item with { Operation = OperatorOperation(item.Operation) }).ToArray() };
    ParcelSnapshot OperatorSnapshot(ParcelSnapshot s) => OperatorSnapshot(s, Management.Status().Authorized);
    static ParcelSnapshot OperatorSnapshot(ParcelSnapshot s, bool authorized) => authorized ? s : s with
    {
        BackupScheduleStatus = null,
        Configuration = s.Configuration with
        {
            // SM settings may read its field-owned values without borrowing a
            // management grant. Originals/history and professional data retain
            // their existing projection and authorization rules.
            Effective = WorkstationConfiguration(s.Configuration.Effective),
            Pending = null,
            InitialRepair = null
        },
        Operations = s.Operations.Select(OperatorOperation).ToArray(),
        // TCP evidence contains actual endpoints for management diagnostics only.
        Devices = s.Devices?.Select(d => d.Source.StartsWith("TCP:client-LF:", StringComparison.Ordinal)
            ? d with { Source = "TCP:client-LF:" + d.DeviceId } : d).ToArray(),
        Health = s.Health.Select(h => h.Detail.StartsWith("TCP:client-LF:", StringComparison.Ordinal)
            ? h with { Detail = "TCP transport established; business admission is separate" } : h).ToArray(),
        Inputs = s.Inputs?.Select(input => input with
        { ReaderBinding = input.ReaderBinding is null ? null : OperatorReader(input.ReaderBinding) }).ToArray()
    };
}
