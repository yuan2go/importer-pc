using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.WcsToPlc.Desktop;
public sealed class WcsToPlcViewModel(IParcelApplication application) : ParcelViewModel(application, "WcsToPlc 作业台", "相机包裹号 → 重量策略 → WCS 格口 → PLC");
public static class Presentation
{
    public static ShellWindow Create(IParcelApplication application, string? view, string? layout)
    {
        ParcelViewModel NewWindow() => new WcsToPlcViewModel(application);
        return new ShellWindow(NewWindow(), NewWindow, () => new Workstation(), view, layout);
    }
}
