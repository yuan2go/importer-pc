using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class ToteStationLaneControl : UserControl
{
    readonly SupplyStationWindow host;
    readonly SupplyStationPositionViewModel lane;
    readonly bool left;
    IReadOnlyList<ToteScanRecord>? shownHistory;
    string? shownLanguage;
    WcsUnpackDetails? shownUnpack;

    internal ToteStationLaneControl(SupplyStationWindow host, SupplyStationPositionViewModel lane)
    {
        this.host = host;
        this.lane = lane;
        left = lane.PositionId == "left";
        InitializeComponent();
        DataContext = lane;
        if (!left)
        {
            LaneBanner.SetResourceReference(Border.BackgroundProperty, "SuccessBackground");
            LaneName.SetResourceReference(TextBlock.ForegroundProperty, "Success");
        }
    }

    internal void Refresh(IReadOnlyList<ToteScanRecord> history)
    {
        var vm = host.ViewModel;
        var position = vm.Snapshot?.Positions?.SingleOrDefault(item => item.Id == lane.PositionId);
        var code = vm.Snapshot?.Configuration.Effective.Wcs?.Parameters.GetValueOrDefault(lane.PositionId + "-bcr-code");
        LaneName.Text = string.IsNullOrWhiteSpace(code) ? lane.Heading : lane.Heading + " · " + code;
        LanePaceText.Text = left ? vm.LeftPaceText : vm.RightPaceText;
        LastScanText.Text = UiText.Format("Tote.LastScan", lane.Input is null ? "—" : lane.ScanTime);
        VehicleText.Text = UiText.Format("Tote.AgvBadge", lane.VehicleStatusText);
        var blocked = !vm.Fresh ? UiText.Get("Work.StateExpired")
            : !lane.SupplyEnabled || !lane.SupplyConfigured ? lane.SupplyNotice
            : vm.Snapshot?.Paused == true ? UiText.Get("Work.InductionPaused")
            : vm.RcsState is not (SupplyStationViewModel.RcsConnectionState.Online or SupplyStationViewModel.RcsConnectionState.Simulation) ? vm.RcsServiceStatus
            : position?.Arrival is not { Confirmed: true } ? UiText.Get("Tote.AgvBlocked")
            : !lane.CanAdmit && !lane.CanSubmitBarcode && lane.CurrentRound is null
                ? lane.DisplayAttentionReason : "";
        BlockedBanner.Visibility = blocked.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        BlockedReason.Text = blocked;

        // Bind the current round's actual response, even when NEED_UNPACK is
        // represented as a business rejection in the original operation.
        var operation = vm.Fresh && lane.CurrentRound is { } active && lane.Operation?.RoundId == active.Id ? lane.Operation : null;
        var destination = operation?.Wcs is { } stage
            ? stage.Replies.LastOrDefault(reply => reply.RequestId == stage.Request.Id && reply.PayloadHash == stage.Request.PayloadHash)?.Destination
            : null;
        var unpack = destination is { Outcome: WcsDestinationOutcome.UnpackRequired } ? destination.Unpack : null;
        SkuText.Text = destination?.SkuId ?? unpack?.SkuId ?? "—";
        QuantityText.Text = destination?.Quantity ?? unpack?.ScanQty.ToString() ?? "—";
        UnpackCard.Visibility = unpack is null ? Visibility.Collapsed : Visibility.Visible;
        ParcelRemoval.Visibility = unpack is null ? Visibility.Visible : Visibility.Collapsed;
        ReadyBanner.Visibility = lane.ShowConfirmedTask ? Visibility.Visible : Visibility.Collapsed;
        var languageChanged = shownLanguage != UiText.Language;
        if (unpack is not null && (unpack != shownUnpack || languageChanged))
        {
            ScanQuantityText.Text = UiText.Format("Tote.ScanQuantity", unpack.ScanQty);
            UnpackLines.ItemsSource = unpack.Chutes.Select(chute => UiText.Format("Tote.UnpackLine", chute.ChuteNo, chute.OrderNo, chute.Remaining)).ToArray();
        }
        LaneStatus.Text = unpack is not null ? UiText.Get("Tote.Unpack")
            : destination is { Outcome: WcsDestinationOutcome.Rejected, Message: { Length: > 0 } raw } ? raw
            : lane.DisplayStatus;
        if (unpack is not null && shownUnpack is null && IsVisible) UnpackCard.BringIntoView();
        shownUnpack = unpack;
        if (!ReferenceEquals(history, shownHistory) || languageChanged)
        {
            shownHistory = history;
            ScanHistory.Header = UiText.Format("Tote.ScanHistoryCount", history.Count);
            HistoryRows.ItemsSource = history.Select(row => new
            {
                Time = row.At.ToLocalTime().ToString("HH:mm:ss"), row.Barcode, Slot = row.Slot ?? "—",
                Status = UiText.Get(row.State switch
                {
                    ParcelState.SlotResolved => "Tote.RecordAssigned",
                    ParcelState.WcsUnknown => "Tote.RecordUnknown",
                    ParcelState.UnpackRequired => "Tote.Unpack",
                    _ => "Tote.RecordRejected"
                }),
                Tone = row.State == ParcelState.SlotResolved ? "Success" : row.State == ParcelState.WcsRejected ? "Danger" : "Warning"
            }).ToArray();
        }
        shownLanguage = UiText.Language;
    }

    internal void ResetHistory() => ScanHistory.IsExpanded = false;

    async void Enable_Click(object sender, RoutedEventArgs args) => await lane.SetSupplyEnabledAsync(true);
    async void Disable_Click(object sender, RoutedEventArgs args) => await lane.SetSupplyEnabledAsync(false);
    async void ConfirmUnpack_Click(object sender, RoutedEventArgs args)
    {
        if (shownUnpack is not null) await lane.ConfirmPlacementRemovalAsync();
    }
    void History_Click(object sender, RoutedEventArgs args) => host.OpenToteHistory();
}
