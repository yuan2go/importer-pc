namespace Ipc.Application.Contracts;

public enum DiagnosticPackageStatus { Complete, Partial }
public sealed record DiagnosticPackageReceipt(string Path, DiagnosticPackageStatus Status, long Bytes,
    string Sha256, DateTimeOffset From, DateTimeOffset Cutoff, string[] Notices);
public sealed record DiagnosticProductInformation(string ApplicationId, string? BusinessVersion, string? SourceCommit,
    string BinarySha256, bool Published, string Integrity, DateTimeOffset CapturedAt);

// Local support export, never a restorable backup or permission to upload.
public interface IDiagnosticPackageApplication
{
    Task<DiagnosticProductInformation> ReadDiagnosticProductInformationAsync(CancellationToken token);
    Task<DiagnosticPackageReceipt> ExportDiagnosticOverviewAsync(string destination, CancellationToken token);
    Task<DiagnosticPackageReceipt> ExportDiagnosticPackageReportAsync(string destination, CancellationToken token);
}
