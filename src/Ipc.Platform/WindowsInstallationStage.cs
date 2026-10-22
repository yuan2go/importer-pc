namespace Ipc.Platform;

// Observations only. DeploymentLocation remains the sole success authority.
public enum WindowsInstallationStage
{
    WaitingForAuthorization, CheckingResponsibilities, PreparingBackup, CopyingFiles,
    PreparingConfiguration, InstallingComponents, RegisteringManagementService,
    CommittingInstallation, CreatingShortcuts, RegisteringApplication, VerifyingReceipt
}

public sealed class InstallationOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner);
