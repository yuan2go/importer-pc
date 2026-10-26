using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public static class WorkstationEntry
{
    // Called once for a new runtime, never for page navigation or child windows.
    // Setup/repair may have authorized the same process-local authority.
    public static async Task BeginRuntimeAsync(IParcelApplication application)
    {
        if (application is IWorkstationAdministration administration && (await administration.ManagementModeStatusAsync()).Authorized)
            await administration.ExitManagementModeAsync();
    }
}
