using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationPosition : UserControl
{
    public SupplyStationPosition()
    {
        InitializeComponent(); InitializeCompositionFeedback();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (DataContext is not SupplyStationPositionViewModel p) return;
            p.SetInputFocused(true);
            // Buttons and selectable values keep their native mouse behaviour.
            // The rest of the card immediately becomes a barcode input surface.
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != this;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is ButtonBase or TextBoxBase or ScrollBar) return;
            FocusBarcodeInput();
        };
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(IsKeyboardFocusWithin);
        };
        TaskFields.IsVisibleChanged += (_, _) => RefreshTaskRegionGeometry();
        DataContextChanged += (_, e) =>
        {
            presentationGeneration++; feedbackShown = readMeasured = slotMeasured = rejectionShown = successShown = null;
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= PositionChanged;
            if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += PositionChanged;
            RefreshBarcodeDisplay(); RefreshSubmitAvailability();
        };
        Loaded += (_, _) => RefreshSubmitAvailability();
        Unloaded += (_, _) => { if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(false); };
        DataObject.AddPastingHandler(BarcodeEntry, (_, e) =>
        {
            PrepareResultReplacement();
            var value = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (submitting || submitted is not null || value is null || value.Any(char.IsControl) ||
                BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + value.Length > 4096)
            { e.CancelCommand(); overflow = true; showingDraftNotice = false; RefreshSubmitAvailability(); InputFeedback.Text = "粘贴无效：只能粘贴一个完整条码（不含换行或Tab，最多4096字符）。请清空后重新输入。"; }
        });
    }
    sealed record InputBasis(Guid Runtime, long Configuration, string? Observation, string? Operation);
    InputBasis? inputBasis;
    SubmitParcelBarcode? submitted;
    bool submitting, overflow, editing, updatingDisplay, showingDraftNotice;
    bool terminalTextRetained;
    string? terminalObservationKey;
    void PositionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.IsProcessing) or nameof(SupplyStationPositionViewModel.ShowConfirmedTask)) RefreshTaskRegionGeometry();
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.CanSubmitBarcode)) RefreshSubmitAvailability();
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.CanViewCurrentPicture)) RefreshPictureAvailability();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.SuccessIdentity) or nameof(SupplyStationPositionViewModel.ReadFeedbackKey) or nameof(SupplyStationPositionViewModel.RejectionIdentity) or nameof(SupplyStationPositionViewModel.ShowConfirmedTask)) ObservePresentation();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.Barcode) or nameof(SupplyStationPositionViewModel.HasCurrentInput) or nameof(SupplyStationPositionViewModel.BarcodeObservationKey))
        {
            RefreshBarcodeDisplay();
            if (editing && showingDraftNotice) InputFeedback.Text = DraftNotice;
        }
    }
    void RefreshTaskRegionGeometry()
    {
        // Retain geometry, never old facts, only across a live request. Errors,
        // pause/offline/stale states reclaim space for actionable instructions.
        if (IsLoaded && DataContext is SupplyStationPositionViewModel { IsProcessing: true } && TaskFields.ActualHeight > 0)
            TaskFields.Height = TaskFields.ActualHeight;
        else TaskFields.Height = double.NaN;
    }
    void RefreshBarcodeDisplay()
    {
        if (editing || submitting || submitted is not null) return;
        if (terminalTextRetained)
        {
            if (DataContext is SupplyStationPositionViewModel current && current.BarcodeObservationKey == terminalObservationKey) return;
            terminalTextRetained = false; RebaseInput.Visibility = Visibility.Collapsed;
        }
        var value = DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p ? p.Barcode : "";
        if (BarcodeEntry.Text != value)
        {
            updatingDisplay = true;
            try { BarcodeEntry.Text = value; }
            finally { updatingDisplay = false; }
        }
        PrepareResultReplacement();
        RefreshPictureAvailability();
    }
    // A displayed result is not a draft. The first character/paste starts a new
    // parcel even if the caret was moved after admission. Once editing begins,
    // ordinary selection, corrections and snapshot refreshes keep the draft.
    void PrepareResultReplacement()
    {
        if (!editing && !submitting && submitted is null) BarcodeEntry.SelectAll();
    }
    internal void FocusBarcodeInput()
    {
        if (!IsVisible || !IsEnabled) return;
        BarcodeEntry.Focus(); PrepareResultReplacement();
    }
    internal void ReceiveScannerText(string text)
    {
        FocusBarcodeInput();
        if (submitting || submitted is not null || BarcodeEntry.IsReadOnly) return;
        if (BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + text.Length > 4096)
        { overflow = true; InputFeedback.Text = "条码超长，请清空后重扫。"; RefreshSubmitAvailability(); return; }
        var caret = BarcodeEntry.SelectionStart + text.Length;
        BarcodeEntry.SelectedText = text;
        BarcodeEntry.Select(caret, 0);
    }
    void Entry_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PrepareResultReplacement();
        if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(true);
    }
    void RefreshSubmitAvailability()
    {
        if (SubmitBarcode is null) return;
        var available = DataContext is SupplyStationPositionViewModel p && p.CanSubmitBarcode;
        if (submitted is not null) available = Host?.ViewModel is { CanUseWorkspace: true, CanStartOperation: true };
        SubmitBarcode.IsEnabled = !submitting && available && (submitted is not null || editing && !overflow && BarcodeEntry.Text.Length > 0);
        RefreshPictureAvailability();
    }
    void RefreshPictureAvailability()
    {
        if (CurrentBarcode is not null) CurrentBarcode.IsEnabled = !editing && !terminalTextRetained && !submitting && submitted is null &&
            DataContext is SupplyStationPositionViewModel { CanViewCurrentPicture: true };
    }
    void CaptureInputBasis()
    {
        if (Host?.ViewModel is not { Fresh: true, Snapshot: { } snapshot } || DataContext is not SupplyStationPositionViewModel p) return;
        inputBasis = new(snapshot.RuntimeId, snapshot.Configuration.Revision, p.Input?.Observation.Id, p.Operation?.Id);
    }
    string DraftNotice => DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p
        ? p.ShowConfirmedTask ? "输入未提交；下方格口属于 " + p.Barcode + "。" : "输入未提交；本位当前读码：" + (p.Barcode.Length == 0 ? "多个或无效条码" : p.Barcode) + "。"
        : "输入未提交。";
    void InputChanged(object sender, TextChangedEventArgs e)
    {
        if (BarcodeEntry is null || updatingDisplay || submitting || submitted is not null) return;
        terminalTextRetained = false; RebaseInput.Visibility = Visibility.Collapsed;
        editing = BarcodeEntry.Text.Length > 0;
        showingDraftNotice = editing; InputFeedback.Text = editing ? DraftNotice : "输入已清空，相机读码将自动更新。";
        if (!editing) { inputBasis = null; overflow = false; }
        else if (inputBasis is null) CaptureInputBasis();
        RefreshSubmitAvailability();
    }
    void RebaseInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null) { InputFeedback.Text = "上次提交结果未确认，请先点“查看提交结果”。"; return; }
        terminalTextRetained = false; editing = BarcodeEntry.Text.Length > 0;
        CaptureInputBasis(); RebaseInput.Visibility = Visibility.Collapsed; InputFeedback.Text = "已确认本位包裹，请重新提交条码。"; BarcodeEntry.Focus(); RefreshSubmitAvailability();
    }
    void ClearInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null) { InputFeedback.Text = "上次提交结果未确认，暂不能清空；请先点“查看提交结果”。"; return; }
        terminalTextRetained = false; editing = false; BarcodeEntry.Clear(); overflow = false; inputBasis = null;
        InputFeedback.Text = "输入已清空，相机读码将自动更新。"; BarcodeEntry.Focus(); RefreshSubmitAvailability();
    }
    async void BarcodeEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true; if (!e.IsRepeat) await SubmitBarcodeAsync();
    }
    void Entry_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (submitting || submitted is not null)
        { e.Handled = true; InputFeedback.Text = "上一次提交还在处理，本次扫描未接收；结果出来后请重扫。"; return; }
        PrepareResultReplacement();
        if (BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + e.Text.Length > 4096 || e.Text.Any(char.IsControl))
        { e.Handled = true; overflow = true; showingDraftNotice = false; RefreshSubmitAvailability(); InputFeedback.Text = "条码过长或含无效字符，已停止接收。请清空后重新输入完整条码。"; }
    }
    async void SubmitBarcode_Click(object sender, RoutedEventArgs e) => await SubmitBarcodeAsync();
    async Task SubmitBarcodeAsync()
    {
        showingDraftNotice = false;
        if (submitting || Host is not { } host || DataContext is not SupplyStationPositionViewModel p) return;
        var vm = host.ViewModel;
        if (!vm.CanStartOperation || !vm.Fresh || vm.Application is not IParcelWorkstation service)
        { InputFeedback.Text = "当前不能提交，请等待状态恢复。"; return; }
        if (submitted is null)
        {
            if (!p.CanSubmitBarcode) { InputFeedback.Text = "本位暂不能接收新条码；请按当前提示处理。"; RefreshSubmitAvailability(); return; }
            if (!editing) { InputFeedback.Text = "框内是上一件的条码；请输入本件条码后再提交。"; return; }
            if (overflow) { InputFeedback.Text = "条码不完整，请清空后重扫或重新输入。"; return; }
            if (BarcodeEntry.Text.Length == 0) { InputFeedback.Text = "请输入完整条码，再按回车或点击提交。"; return; }
            if (inputBasis is null) CaptureInputBasis();
            if (inputBasis is not { } basis) { InputFeedback.Text = "本位状态未读取，请稍后再提交。"; return; }
            submitted = new(Wire.Id(), basis.Runtime, basis.Configuration, p.PositionId, BarcodeEntry.Text,
                ParcelBarcodeInputKind.Keyboard, basis.Observation, basis.Operation);
        }
        var request = submitted; submitting = true; BarcodeEntry.IsReadOnly = true;
        SubmitBarcode.IsEnabled = false; RebaseInput.IsEnabled = false;
        InputFeedback.Text = "正在提交，请等待结果，勿重复扫描。";
        await vm.RunAsync(async () =>
        {
            try
            {
                var receipt = await vm.ServiceAsync(() => service.SubmitParcelBarcodeAsync(request, CancellationToken.None));
                var label = receipt.Status switch { "Admitted" => "已受理", "Rejected" => "未受理", "Blocked" => "暂不能受理", "Conflict" => "本位状态已变化，请确认后重交", "Duplicate" => "重复扫描", "Pending" => "受理结果未确认", _ => receipt.Status };
                InputFeedback.Text = label + " · " + Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.Notice(receipt.Detail) + (receipt.OperationId is null ? "" : "\n作业：" + receipt.OperationId) +
                    (receipt.Status == "Pending" ? "\n命令：" + request.CommandId : "");
                vm.ReportBarcodeFeedback(receipt, InputFeedback.Text);
                if (receipt.Status != "Pending")
                {
                    submitted = null;
                    editing = false; inputBasis = null;
                    terminalTextRetained = receipt.Status != "Admitted";
                    terminalObservationKey = p.BarcodeObservationKey;
                    PrepareResultReplacement();
                    if (receipt.Status == "Conflict") RebaseInput.Visibility = Visibility.Visible;
                    if (receipt.Status == "Admitted") { editing = false; inputBasis = null; InputFeedback.Text = ""; }
                }
            }
            catch (ArgumentException error) { submitted = null; InputFeedback.Text = error.Message; }
            catch (Exception error)
            {
                InputFeedback.Text = "提交结果未确认，系统不会自动重发。请点“查看提交结果”，或在记录中查找本件。命令：" + request.CommandId + "\n" + error.Message;
                throw;
            }
        }, "提交或核对本位条码");
        submitting = false; BarcodeEntry.IsReadOnly = submitted is not null;
        SubmitBarcode.Content = submitted is null ? "提交" : "查看提交结果";
        RefreshSubmitAvailability(); RebaseInput.IsEnabled = submitted is null; RefreshBarcodeDisplay(); PrepareResultReplacement();
    }
    void Picture_Click(object sender, RoutedEventArgs e)
    {
        if (editing || terminalTextRetained || submitting || submitted is not null || Host is not { } host ||
            DataContext is not SupplyStationPositionViewModel { CurrentPictureQuery: { } query } p) return;
        Ipc.Application.ParcelFlow.Desktop.ParcelPictureWindow.Open(host, host.ViewModel, query, p.Barcode);
    }
    SupplyStationWindow? Host => Window.GetWindow(this) as SupplyStationWindow;
    void New_Click(object sender, RoutedEventArgs e) { if (DataContext is SupplyStationPositionViewModel p) p.NewParcel(); }
    async void Emit_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.EmitPositionAsync(p); }
    async void Details_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel { Operation: { } op }) await h.ShowDetailsAsync(op); }
    void Diagnostics_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) h.ShowPositionDiagnostics(p); }
    async void Reconcile_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ReconcilePositionAsync(p); }
    async void Continue_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ContinuePositionAsync(p); }
}
