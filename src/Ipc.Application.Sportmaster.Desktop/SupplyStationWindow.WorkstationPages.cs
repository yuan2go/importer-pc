using System.Windows;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    ToteSupplyStationWindow? toteWorkstation;
    public Action<ToteSessionSummary>? RecordToteSession { get; set; }

    FrameworkElement WorkstationContent() => ActiveWorkstationPage switch
    {
        WorkstationPages.SupplyStation => workContent,
        WorkstationPages.ToteStation => toteWorkstation ??= new(this, new LocalToteSessionController(summary => RecordToteSession?.Invoke(summary))),
        _ => throw new ArgumentException(UiText.Format("Page.Invalid", ActiveWorkstationPage))
    };

    bool ShowingTote => ActiveWorkstationPage == WorkstationPages.ToteStation && ViewModel.SelectedPage == "workstation";
    internal bool ToteMuted => cueVolume == 0;
    internal void ToggleToteMute() => ApplyCueVolume(ToteMuted ? resumeCueVolume : 0);
    internal void OpenToteSettings() => StationSettings_Click(this, new RoutedEventArgs());
    internal void OpenToteHistory() => History_Click(this, new RoutedEventArgs());
    internal void FocusToteLane() => (RecentPosition() ?? ActiveLanes.FirstOrDefault(lane =>
        lane.DataContext is SupplyStationPositionViewModel { SupplyEnabled: true }))?.FocusBarcodeInput();

    void UpdateWorkstationChrome()
    {
        if (ShowingTote)
        {
            ClassicHeader.Visibility = DailyNavigation.Visibility = NavigationToggle.Visibility = WorkstationFooter.Visibility = Visibility.Collapsed;
        }
        else
        {
            ClassicHeader.ClearValue(VisibilityProperty);
            DailyNavigation.ClearValue(VisibilityProperty);
            NavigationToggle.ClearValue(VisibilityProperty);
            WorkstationFooter.ClearValue(VisibilityProperty);
        }
    }

    void RefreshWorkstationContent() => toteWorkstation?.Refresh();
}
