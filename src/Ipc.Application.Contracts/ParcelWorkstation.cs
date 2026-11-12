using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

public enum ParcelBarcodeInputKind { Keyboard, PdaKeyboard }
// Identity belongs to one deliberate submission, not to the barcode string.
public sealed record SubmitParcelBarcode(string CommandId, Guid RuntimeId, long ConfigurationRevision,
    string PositionId, string Barcode, ParcelBarcodeInputKind Kind, string? ExpectedObservationId, string? ExpectedOperationId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedRoundId { get; init; }
    public string? ExpectedArrivalGeneration { get; init; }
    public string? ExpectedPlacementBoundary { get; init; }
}
public sealed record ParcelBarcodeReceipt(string Status, string Detail, string? OperationId, string? ObservationId, long Revision);
public sealed record ParcelPictureQuery(string? OperationId = null, string? ObservationId = null, string? RoundId = null);
public sealed record ParcelPictureResult(string Status, string Detail, string? OperationId, string? ObservationId, CameraOriginalImage? Image)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArchivedParcelPicture? Archived { get; init; }
}
public sealed record ArchivedParcelPicture(string? OperationId, OriginalFrameArchive Metadata, byte[] Png)
{
    public string? RoundId { get; init; }
}
public interface IParcelWorkstation
{
    Task<ParcelBarcodeReceipt> SubmitParcelBarcodeAsync(SubmitParcelBarcode command, CancellationToken token);
    Task<ParcelPictureResult> ReadParcelPictureAsync(ParcelPictureQuery query, CancellationToken token);
}
