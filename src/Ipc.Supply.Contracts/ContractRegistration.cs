using Ipc.Platform;
namespace Ipc.Supply;

public static class SupplyContractRegistration
{
    public static void Register() => ExtensionPublicContracts.Register(typeof(IIntegrationExtension).Assembly);
}
