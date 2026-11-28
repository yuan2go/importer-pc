using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster;

public sealed partial class SportmasterFlow
{
    public override bool UsesIndependentPositionControl => true;
    public override bool UsesArrivalRounds => true;
    public override bool UsesTrustedTrayPresence => true;
    public override bool SupportsBarcodeFiltering => true;
    public override bool SupportsOperationsShiftPolicy => true;
    public override bool SupportsManualRcsDebug => true;
    public override IReadOnlyList<string> AllowedWorkstationPages => Pages;
    public override bool UsesDeferredConnections => true;
    public override bool ValidatesWeightRange => false;
    public override bool SupportsCameraDiscovery => true;
    public override string AdmissionCheckLabel => "设置提交检查";

    static IReadOnlyList<string> Pages { get; } = Array.AsReadOnly(new[] { WorkstationPages.SupplyStation, WorkstationPages.ToteStation });

    public override string[] ValidateFlowConfiguration(ParcelConfiguration configuration)
    {
        if (configuration.ApplicationId == Id && configuration.BackupSchedule is { } schedule &&
            !string.IsNullOrWhiteSpace(schedule.Directory) && Path.IsPathFullyQualified(schedule.Directory))
            try { InstallationIdentity.RequireExternalFilePath(schedule.Directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return [error.Message]; }
        return [];
    }

    public override ParcelConfiguration ProjectWorkstationConfiguration(ParcelConfiguration original, ParcelConfiguration masked)
    {
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
}
