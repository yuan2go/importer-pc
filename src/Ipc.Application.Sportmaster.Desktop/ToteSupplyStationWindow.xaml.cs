using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// A work surface in the existing window. The host owns device connections,
// settings/exit dialogs, sound, keyboard capture, and the publication stream.
public partial class ToteSupplyStationWindow : UserControl
{
    readonly SupplyStationWindow host;
    readonly IToteSessionController tote;
    readonly ToteStationLaneControl leftLane, rightLane;
    string? noticeKey;

    internal SupplyStationPosition Left { get; }
    internal SupplyStationPosition Right { get; }

    internal ToteSupplyStationWindow(SupplyStationWindow host, IToteSessionController tote)
    {
        this.host = host;
        this.tote = tote;
        InitializeComponent();
        DataContext = host.ViewModel;
        leftLane = new(host, host.ViewModel.Left);
        rightLane = new(host, host.ViewModel.Right);
        Left = new(leftLane) { DataContext = host.ViewModel.Left, Margin = new(0, 0, 4, 0) };
        Right = new(rightLane) { DataContext = host.ViewModel.Right, Margin = new(4, 0, 0, 0) };
        Grid.SetColumn(Right, 1);
        Lanes.Children.Add(Left);
        Lanes.Children.Add(Right);
        ContainerInput.Text = tote.ContainerCode;
        PlanInput.Text = tote.PlannedCount?.ToString(CultureInfo.InvariantCulture) ?? "";
        ContainerInput.LostKeyboardFocus += (_, _) => CommitInputs();
        PlanInput.LostKeyboardFocus += (_, _) => CommitInputs();
        DataObject.AddPastingHandler(ContainerInput, (_, args) =>
        {
            if (args.SourceDataObject.GetData(DataFormats.UnicodeText) is string text && !text.Any(char.IsControl)) return;
            args.CancelCommand();
            ShowNotice("Tote.InvalidCode");
        });
        ContainerInput.PreviewTextInput += (_, args) => args.Handled = args.Text.Any(char.IsControl);
        Loaded += (_, _) => Refresh();
    }

    internal void Refresh()
    {
        if (host.ViewModel is { Fresh: true, Snapshot: { } snapshot }) tote.Observe(snapshot.Operations);
        if (!IsVisible) return;
        var now = DateTimeOffset.Now;
        ClockText.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        ClockText.ToolTip = now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        MuteButton.Content = UiText.Get(host.ToteMuted ? "Tote.SoundOn" : "Tote.Mute");
        CountText.Text = tote.DispatchedCount.ToString(CultureInfo.InvariantCulture);
        SessionFeedback.Text = noticeKey is null ? "" : UiText.Get(noticeKey);
        leftLane.Refresh(tote.LeftHistory);
        rightLane.Refresh(tote.RightHistory);
    }

    bool CommitInputs()
    {
        var plan = PlanInput.Text.Trim();
        int? count = null;
        if (plan.Length != 0)
        {
            if (!int.TryParse(plan, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                ShowNotice("Tote.InvalidPlan"); return false;
            }
            count = value;
        }
        try { tote.ContainerCode = ContainerInput.Text.Trim(); }
        catch (ArgumentException) { ShowNotice("Tote.InvalidCode"); return false; }
        catch (InvalidOperationException) { ShowNotice("Tote.FinishBeforeChanging"); return false; }
        tote.PlannedCount = count;
        ShowNotice(null);
        return true;
    }

    void ShowNotice(string? key)
    {
        noticeKey = key;
        SessionFeedback.Text = key is null ? "" : UiText.Get(key);
    }

    void ToteInput_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        if (CommitInputs()) host.FocusToteLane();
    }

    void EndSupply_Click(object sender, RoutedEventArgs args)
    {
        if (!CommitInputs()) return;
        Refresh();
        if (tote.HasShortage && new ToteEndSupplyConfirmWindow(tote) { Owner = host }.ShowDialog() != true) return;
        try { tote.ResetSession(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceError("ToteSessionEnded: {0}", DiagnosticText.Protect(error.Message));
            ShowNotice("Tote.EndFailed");
            return;
        }
        ContainerInput.Text = "";
        PlanInput.Text = "";
        leftLane.ResetHistory();
        rightLane.ResetHistory();
        ShowNotice("Tote.Ended");
        Refresh();
    }

    async void CheckRcs_Click(object sender, RoutedEventArgs args) => await host.ViewModel.CheckRcsAsync();
    void Mute_Click(object sender, RoutedEventArgs args) { host.ToggleToteMute(); Refresh(); }
    void Settings_Click(object sender, RoutedEventArgs args) => host.OpenToteSettings();
    void History_Click(object sender, RoutedEventArgs args) => host.OpenToteHistory();
    void Exit_Click(object sender, RoutedEventArgs args) => host.Close();
}
