using System.Windows;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;
public static class Presentation
{
    public static Window Create(IParcelApplication application, string? view, string? layout, string ui = "supply-station", Func<Task>? stopOwner = null)
    {
        if (ui != "supply-station") throw new ArgumentException("此供包业务仅支持当前作业界面：" + ui);
        return new SupplyStationWindow(application, view, layout, stopOwner);
    }
}
