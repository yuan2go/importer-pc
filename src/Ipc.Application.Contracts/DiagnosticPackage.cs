namespace Ipc.Application.Contracts;

public enum DiagnosticPackageStatus { Complete, Partial }
public sealed record DiagnosticPackageReceipt(string Path, DiagnosticPackageStatus Status, long Bytes,
    string Sha256, DateTimeOffset From, DateTimeOffset Cutoff, string[] Notices);

// Local support export, never a restorable backup or permission to upload.
public interface IDiagnosticPackageApplication
{
    Task<DiagnosticPackageReceipt> ExportDiagnosticPackageReportAsync(string destination, CancellationToken token);
}
