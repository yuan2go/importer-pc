using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationPosition : UserControl
{
    public SupplyStationPosition()
    {
        InitializeComponent(); InitializeCompositionFeedback();
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
    ParcelBarcodeInputKind inputKind = ParcelBarcodeInputKind.Keyboard;
    void PositionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.CanSubmitBarcode)) RefreshSubmitAvailability();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.SuccessIdentity) or nameof(SupplyStationPositionViewModel.ReadFeedbackKey) or nameof(SupplyStationPositionViewModel.RejectionIdentity) or nameof(SupplyStationPositionViewModel.ShowConfirmedTask)) ObservePresentation();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.Barcode) or nameof(SupplyStationPositionViewModel.HasCurrentInput))
        {
            RefreshBarcodeDisplay();
            if (editing && showingDraftNotice) InputFeedback.Text = DraftNotice;
        }
    }
    void RefreshBarcodeDisplay()
    {
        if (editing || submitting || submitted is not null) return;
        var value = DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p ? p.Barcode : "";
        if (BarcodeEntry.Text != value)
        {
            updatingDisplay = true;
            try { BarcodeEntry.Text = value; }
            finally { updatingDisplay = false; }
        }
        PrepareResultReplacement();
    }
    // A displayed result is not a draft. The first character/paste starts a new
    // parcel even if the caret was moved after admission. Once editing begins,
    // ordinary selection, corrections and snapshot refreshes keep the draft.
    void PrepareResultReplacement()
    {
        if (!editing && !submitting && submitted is null) BarcodeEntry.SelectAll();
    }
    void Entry_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PrepareResultReplacement();
        if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(true);
    }
    void Entry_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(false);
    }
    void InputSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BarcodeEntry is null || KeyboardSource is null || VisibleInputSource is null) return;
        SetInputKind(VisibleInputSource.SelectedIndex == 1 ? ParcelBarcodeInputKind.PdaKeyboard : ParcelBarcodeInputKind.Keyboard);
        if (IsLoaded && !submitting && submitted is null) BarcodeEntry.Focus();
    }
    void RefreshSubmitAvailability()
    {
        if (SubmitBarcode is null) return;
        var available = DataContext is SupplyStationPositionViewModel p && p.CanSubmitBarcode;
        if (submitted is not null) available = Host?.ViewModel is { CanUseWorkspace: true, CanStartOperation: true };
        SubmitBarcode.IsEnabled = !submitting && available && (submitted is not null || editing && !overflow && BarcodeEntry.Text.Length > 0);
        if (VisibleInputSource is not null) VisibleInputSource.IsEnabled = !submitting && submitted is null;
    }
    void KeyboardSource_Click(object sender, RoutedEventArgs e) => SetInputKind(ParcelBarcodeInputKind.Keyboard);
    void PdaSource_Click(object sender, RoutedEventArgs e) => SetInputKind(ParcelBarcodeInputKind.PdaKeyboard);
    void SetInputKind(ParcelBarcodeInputKind value)
    {
        if (submitting || submitted is not null) return;
        inputKind = value;
        KeyboardSource.IsChecked = value == ParcelBarcodeInputKind.Keyboard;
        PdaSource.IsChecked = value == ParcelBarcodeInputKind.PdaKeyboard;
        var index = value == ParcelBarcodeInputKind.PdaKeyboard ? 1 : 0;
        if (VisibleInputSource.SelectedIndex != index) VisibleInputSource.SelectedIndex = index;
    }
    void CaptureInputBasis()
    {
        if (Host?.ViewModel is not { Fresh: true, Snapshot: { } snapshot } || DataContext is not SupplyStationPositionViewModel p) return;
        inputBasis = new(snapshot.RuntimeId, snapshot.Configuration.Revision, p.Input?.Observation.Id, p.Operation?.Id);
    }
    string DraftNotice => DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p
        ? "输入未提交；下方格口仍属于上一件 " + p.Barcode + "。" : "输入未提交。";
    void InputChanged(object sender, TextChangedEventArgs e)
    {
        if (BarcodeEntry is null || updatingDisplay || submitting || submitted is not null) return;
        editing = true;
        showingDraftNotice = true; InputFeedback.Text = DraftNotice;
        if (BarcodeEntry.Text.Length == 0) { inputBasis = null; overflow = false; }
        else if (inputBasis is null) CaptureInputBasis();
        RefreshSubmitAvailability();
    }
    void RebaseInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null) { InputFeedback.Text = "上次提交结果未确认，请先点“查看提交结果”。"; return; }
        CaptureInputBasis(); RebaseInput.Visibility = Visibility.Collapsed; InputFeedback.Text = "已确认本位包裹，请重新提交条码。"; BarcodeEntry.Focus();
    }
    void ClearInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null) { InputFeedback.Text = "上次提交结果未确认，暂不能清空；请先点“查看提交结果”。"; return; }
        editing = true; BarcodeEntry.Clear(); overflow = false; inputBasis = null; InputFeedback.Text = "已清空输入，本位当前包裹不受影响。"; BarcodeEntry.Focus();
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
            if (BarcodeEntry.Text.Length == 0) { InputFeedback.Text = "请输入完整条码；PDA 需设为键盘输入并以回车结束。"; return; }
            if (inputBasis is null) CaptureInputBasis();
            if (inputBasis is not { } basis) { InputFeedback.Text = "本位状态未读取，请稍后再提交。"; return; }
            submitted = new(Wire.Id(), basis.Runtime, basis.Configuration, p.PositionId, BarcodeEntry.Text,
                inputKind, basis.Observation, basis.Operation);
        }
        var request = submitted; submitting = true; BarcodeEntry.IsReadOnly = true; KeyboardSource.IsEnabled = PdaSource.IsEnabled = false;
        SubmitBarcode.IsEnabled = false; RebaseInput.IsEnabled = false;
        InputFeedback.Text = "正在提交，请等待结果，勿重复扫描。";
        await vm.RunAsync(async () =>
        {
            try
            {
                var receipt = await vm.ServiceAsync(() => service.SubmitParcelBarcodeAsync(request, CancellationToken.None));
                var label = receipt.Status switch { "Admitted" => "已受理", "Rejected" => "未受理", "Blocked" => "暂不能受理", "Conflict" => "本位状态已变化，请确认后重交", "Duplicate" => "重复扫描", "Pending" => "结果待核对", _ => receipt.Status };
                InputFeedback.Text = label + " · " + Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.Notice(receipt.Detail) + (receipt.OperationId is null ? "" : "\n作业：" + receipt.OperationId) +
                    (receipt.Status == "Pending" ? "\n命令：" + request.CommandId : "");
                vm.ReportBarcodeFeedback(receipt, InputFeedback.Text);
                if (receipt.Status != "Pending")
                {
                    submitted = null;
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
        }, "提交或核对本位人工条码");
        submitting = false; BarcodeEntry.IsReadOnly = submitted is not null; KeyboardSource.IsEnabled = PdaSource.IsEnabled = submitted is null;
        SubmitBarcode.Content = submitted is null ? "提交" : "查看提交结果";
        RefreshSubmitAvailability(); RebaseInput.IsEnabled = submitted is null; RefreshBarcodeDisplay();
    }
    void Picture_Click(object sender, RoutedEventArgs e)
    {
        if (Host is not { } host || DataContext is not SupplyStationPositionViewModel p) return;
        var query = p.Input is { } input ? new ParcelPictureQuery(ObservationId: input.Observation.Id) :
            p.Operation is { } op ? new ParcelPictureQuery(OperationId: op.Id) : null;
        if (query is not null) Ipc.Application.ParcelFlow.Desktop.ParcelPictureWindow.Open(host, host.ViewModel, query, p.Barcode);
    }
    SupplyStationWindow? Host => Window.GetWindow(this) as SupplyStationWindow;
    void New_Click(object sender, RoutedEventArgs e) { if (DataContext is SupplyStationPositionViewModel p) p.NewParcel(); }
    async void Emit_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.EmitPositionAsync(p); }
    async void Details_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel { Operation: { } op }) await h.ShowDetailsAsync(op); }
    void Diagnostics_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) h.ShowPositionDiagnostics(p); }
    async void Reconcile_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ReconcilePositionAsync(p); }
    async void Continue_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ContinuePositionAsync(p); }
}
