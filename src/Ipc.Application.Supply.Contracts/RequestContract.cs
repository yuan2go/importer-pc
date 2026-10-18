namespace Ipc.Supply;

// Business intent and frozen authorization facts. No transport fields or synthetic quantity policy.
public sealed record RequestIntent(string StationId, string SessionId, string OwnerId, long OwnerRevision,
    RequestKind Kind, string Barcode, int? Package, int? Quantity, string? CheckToken, Actor Actor);
public sealed record RequestAuthority(Actor Actor, int? Package, int? Quantity);
public interface IRequestContract
{
    IntegrationImplementation? Freeze() => null;
    string Encode(RequestIntent intent, string scenario);
    RequestAuthority Authority(BusinessRequest request);
    void ValidateResponse(BusinessRequest request, IntegrationResponse response);
}
