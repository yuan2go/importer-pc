namespace Ipc.Platform;

public static class ExtensionContractCompatibility
{
    public static bool Accepts(ExtensionManifest manifest) =>
        manifest.HostContractSha256 == Wire.FileHash(typeof(IObservationExtension).Assembly.Location);
}
