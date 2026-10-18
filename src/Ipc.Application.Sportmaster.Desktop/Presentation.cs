using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;
public sealed class SportmasterViewModel(IParcelApplication application) : ParcelViewModel(application, "Sportmaster 作业台", "相机包裹号 → WCS 格口 → 页面展示");
public static class Presentation
{
    public static ShellWindow Create(IParcelApplication application, string? view, string? layout)
    {
        ParcelViewModel NewWindow() => new SportmasterViewModel(application);
        return new ShellWindow(NewWindow(), NewWindow, () => new Workstation(), view, layout);
    }
}
