using Ipc.Platform;
using Ipc.Inspection;
namespace Ipc.Assembly;
static class InstalledBusiness
{
    public static BusinessAssembly Create() => new("inspection", launch => new InspectionRuntime(launch));
}
