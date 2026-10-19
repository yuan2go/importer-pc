using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.DirectPlc.Desktop;
public sealed class DirectPlcViewModel(IParcelApplication application) : ParcelViewModel(application, "Importer PC · DirectPlc 作业台", "相机包裹号 → 重量策略 → PLC", "direct-plc");
public static class Presentation
{
    public static ShellWindow Create(IParcelApplication application, string? view, string? layout)
    {
        ParcelViewModel NewWindow() => new DirectPlcViewModel(application);
        return new ShellWindow(NewWindow(), NewWindow, () => new Workstation(), view, layout);
    }
}
