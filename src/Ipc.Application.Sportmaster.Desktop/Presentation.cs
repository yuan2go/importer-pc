using System.Windows;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;
public sealed class SportmasterViewModel(IParcelApplication application) : ParcelViewModel(application, "Sportmaster 作业台", "相机包裹号 → WCS 格口 → 页面展示");
public static class Presentation
{
    public static Window Create(IParcelApplication application, string? view, string? layout, string ui = "classic", Func<Task>? stopOwner = null)
    {
        if (ui == "supply-station") return new SupplyStationWindow(application, view, layout, stopOwner);
        if (ui != "classic") throw new ArgumentException("未知 Sportmaster UI：" + ui);
        ParcelViewModel NewWindow() => new SportmasterViewModel(application);
        return new ShellWindow(NewWindow(), NewWindow, () => new Workstation(), view, layout) { StopOwnerAsync = stopOwner };
    }
}
