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
    static ParcelConfiguration OperatorConfiguration(ParcelConfiguration c) => c with
    {
        Reader = OperatorReader(c.Reader), Scale = OperatorBinding(c.Scale), Wcs = OperatorBinding(c.Wcs), Plc = OperatorBinding(c.Plc),
        Login = OperatorBinding(c.Login), MaintenanceLogin = null, CapabilityInstances = null, Extensions = null,
        BarcodeFilter = null, BackupSchedule = null,
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
        FrozenConfiguration = OperatorConfiguration(op.FrozenConfiguration), Wcs = OperatorStage(op.Wcs), Plc = OperatorStage(op.Plc),
        Capture = OperatorStage(op.Capture), Upload = OperatorStage(op.Upload), BarcodeSelection = null,
        ReaderBinding = op.ReaderBinding is null ? null : OperatorReader(op.ReaderBinding)
    };
    ParcelHistoryPage OperatorHistory(ParcelHistoryPage page) => Management.Status().Authorized ? page : page with
    { Items = page.Items.Select(item => item with { Operation = OperatorOperation(item.Operation) }).ToArray() };
    ParcelSnapshot OperatorSnapshot(ParcelSnapshot s) => Management.Status().Authorized ? s : s with
    {
        Configuration = s.Configuration with
        {
            Effective = OperatorConfiguration(s.Configuration.Effective), Pending = null, InitialRepair = null,
            Confirmation = s.Configuration.Confirmation is null ? null : s.Configuration.Confirmation with { OriginalEnvelopeBase64 = null }
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
