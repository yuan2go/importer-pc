using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum ParcelBarcodeInputKind { Keyboard, PdaKeyboard }
// Identity belongs to one deliberate submission, not to the barcode string.
public sealed record SubmitParcelBarcode(string CommandId, Guid RuntimeId, long ConfigurationRevision,
    string PositionId, string Barcode, ParcelBarcodeInputKind Kind, string? ExpectedObservationId, string? ExpectedOperationId);
public sealed record ParcelBarcodeReceipt(string Status, string Detail, string? OperationId, string? ObservationId, long Revision);
public sealed record ParcelPictureQuery(string? OperationId = null, string? ObservationId = null);
public sealed record ParcelPictureResult(string Status, string Detail, string? OperationId, string? ObservationId, CameraOriginalImage? Image)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArchivedParcelPicture? Archived { get; init; }
}
public sealed record ArchivedParcelPicture(string OperationId, OriginalFrameArchive Metadata, byte[] Png);
public interface IParcelWorkstation
{
    Task<ParcelBarcodeReceipt> SubmitParcelBarcodeAsync(SubmitParcelBarcode command, CancellationToken token);
    Task<ParcelPictureResult> ReadParcelPictureAsync(ParcelPictureQuery query, CancellationToken token);
}
