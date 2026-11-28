using System.Reflection;
using System.Windows;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

public static class Presentation
{
    public static string BuildDefaultWorkstationPage { get; } = typeof(Presentation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == WorkstationPages.MetadataKey)?.Value ?? WorkstationPages.SupplyStation;

    public static Window Create(IParcelApplication application, string? view, string? layout, string ui = WorkstationPages.SupplyStation,
        Func<Task>? stopOwner = null, bool followEffectivePage = false)
    {
        if (ui is not (WorkstationPages.SupplyStation or WorkstationPages.ToteStation))
            throw new ArgumentException(UiText.Format("Page.Invalid", ui), nameof(ui));
        return new SupplyStationWindow(application, view, layout, stopOwner, workstationPage: ui, followEffectivePage: followEffectivePage);
    }
}
