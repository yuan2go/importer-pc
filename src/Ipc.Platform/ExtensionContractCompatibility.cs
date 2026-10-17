namespace Ipc.Platform;

public static class ExtensionContractCompatibility
{
    // DELIVERY-12 published contract fingerprint, candidate-inventory.json, source 11f0728.
    // Contract1 observation ABI is retained; this exception cannot admit integration modules.
    public const string Delivery12 = "259CD65594630D5A48D4EB0CB5E28B9C2603403BBB42D50B63D9696203024E8D";
    public static bool Accepts(ExtensionManifest manifest) => manifest.HostContractSha256 == Wire.FileHash(typeof(IObservationExtension).Assembly.Location) ||
        manifest.HostContractSha256 == Delivery12 && manifest.ContractMajor == 1 && manifest.Modules is not null && manifest.Modules.All(x => x is not null && x.Kind == "observation");
}
