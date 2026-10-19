using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.WcsToPlc.Desktop;
public sealed class WcsToPlcViewModel(IParcelApplication application) : ParcelViewModel(application, "Importer PC · WcsToPlc 作业台", "相机包裹号 → 重量策略 → WCS 格口 → PLC", "wcs-to-plc");
public static class Presentation
{
    public static ShellWindow Create(IParcelApplication application, string? view, string? layout)
    {
        ParcelViewModel NewWindow() => new WcsToPlcViewModel(application);
        return new ShellWindow(NewWindow(), NewWindow, () => new Workstation(), view, layout);
    }
}
