using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Adapters;

// SIMULATION-v1 persisted JSON shape is unchanged, including legacy fixture fields.
public sealed record FixturePayload(string StationId, string SessionId, string OwnerId, long OwnerRevision,
    RequestKind Kind, string Barcode, int? Package, int? Quantity, string? CheckToken,
    string Policy, int InitialDemand, string Fault, Actor Actor);

public sealed class SimulationRequestContract : IRequestContract
{
    readonly StationConfig config;
    public SimulationRequestContract(StationConfig config)
    {
        if (config.Mode != RunMode.Simulation || config.Integration.Contract != "SIMULATION-v1" || config.Policy != "SYNTHETIC-KEEP-v1")
            throw new InvalidDataException("No approved simulation contract/policy; production has no implementation");
        this.config = config;
    }
    public string Encode(RequestIntent intent, string scenario) => Wire.Encode(new FixturePayload(intent.StationId, intent.SessionId,
        intent.OwnerId, intent.OwnerRevision, intent.Kind, intent.Barcode, intent.Package, intent.Quantity, intent.CheckToken,
        config.Policy, config.FixtureDemand, scenario, intent.Actor));
    static FixturePayload Decode(BusinessRequest request)
    {
        if (request.Binding.Contract != "SIMULATION-v1" || Wire.Hash(request.Payload) != request.PayloadHash) throw new InvalidDataException("Original simulation contract/hash mismatch");
        return Wire.Decode<FixturePayload>(request.Payload);
    }
    public RequestAuthority Authority(BusinessRequest request)
    {
        var payload = Decode(request); return new(payload.Actor, payload.Package, payload.Quantity);
    }
    public void ValidateResponse(BusinessRequest request, IntegrationResponse response)
    {
        if (response.RequestId != request.Id || response.PayloadHash != request.PayloadHash || response.Result is not ("Accepted" or "Rejected") || string.IsNullOrWhiteSpace(response.EvidenceId)) throw new InvalidDataException("Response identity/hash/classification mismatch");
        // Optional semantic snapshots preserve compatibility with existing SIMULATION-v1 receipts.
        // An absent snapshot remains unknown; a present one must carry its own source evidence.
        if ((response.Context is not null && response.Context.Provenance is null) || (response.Task is not null && response.Task.Provenance is null)) throw new InvalidDataException("Semantic snapshot provenance required");
        ValidateProvenance(response.Context?.Provenance, response.EvidenceId);
        ValidateProvenance(response.Task?.Provenance, response.EvidenceId);
        if (response.Context is not null && request.Kind != RequestKind.Context) throw new InvalidDataException("Context snapshot on unrelated response");
        if (response.Task is not null && request.Kind is not (RequestKind.Validate or RequestKind.Submit)) throw new InvalidDataException("Item snapshot on unrelated response");
        if (response.Result != "Accepted") return;
        var payload = Decode(request);
        if (request.Kind == RequestKind.Submit && (response.Quantity != payload.Quantity || response.Quantity is not > 0)) throw new InvalidDataException("Acceptance quantity mismatch");
        if (request.Kind == RequestKind.Validate && (response.Quantity is null or < 0 || response.Quantity > payload.Package || response.Remaining is null or < 0)) throw new InvalidDataException("Validation quantity missing/invalid");
        if (request.Kind == RequestKind.CheckClose && (response.Remaining is null or < 0 || response.CheckToken is null)) throw new InvalidDataException("Close snapshot missing");
    }
    static void ValidateProvenance(Provenance? provenance, string evidenceId)
    {
        if (provenance is null) return;
        if (string.IsNullOrWhiteSpace(provenance.Source) || string.IsNullOrWhiteSpace(provenance.Version) ||
            provenance.EvidenceId != evidenceId || provenance.AcquiredAt == default)
            throw new InvalidDataException("Semantic snapshot provenance missing/invalid");
    }
}
