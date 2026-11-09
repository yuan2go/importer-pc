namespace Ipc.Platform;

// Observations only. DeploymentLocation remains the sole success authority.
public enum WindowsInstallationStage
{
    WaitingForAuthorization, CheckingInstallation, CopyingFiles = 3,
    PreparingConfiguration, InstallingComponents, RegisteringManagementService,
    CommittingInstallation, CreatingShortcuts, RegisteringApplication, VerifyingReceipt, InstallingCameraRuntime
}

public sealed class InstallationOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner);
