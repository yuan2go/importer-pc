namespace Ipc.Application.Contracts;

// Preparation is pure: the application persists these exact UTF8 bytes in T1.
// A sequence is assigned by the original database, never by a transport attempt.
public interface IWcsRequestPreparer
{
    PreparedIntegrationRequest PrepareRequest(FrozenRequest request, long operationSequence);
}
public sealed record PreparedIntegrationRequest(long RequestId, string ContentType, string Body, string BodyHash);
public sealed record IntegrationHttpResponse(int StatusCode, string BodyBase64, string BodyHash, bool Complete = true);
public enum WcsDestinationOutcome { Assigned, UnpackRequired, Rejected }
public sealed record WcsUnpackChute(string ChuteNo, string OrderNo, int Remaining, string Phase);
public sealed record WcsUnpackDetails(string Barcode, string SkuId, int ScanQty, WcsUnpackChute[] Chutes);
public sealed record WcsDestinationResult(WcsDestinationOutcome Outcome, string? Code, string? Message, string? ArgsJson,
    string? TaskId, string? SkuId, decimal? Length, decimal? Width, decimal? Height, decimal? Weight, WcsUnpackDetails? Unpack);
