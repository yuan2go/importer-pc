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
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= PositionChanged;
            if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += PositionChanged;
            RefreshBarcodeDisplay();
        };
        DataObject.AddPastingHandler(BarcodeEntry, (_, e) =>
        {
            PrepareResultReplacement();
            var value = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (submitting || submitted is not null || value is null || value.Any(char.IsControl) ||
                BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + value.Length > 4096)
            { e.CancelCommand(); overflow = true; InputFeedback.Text = "未接纳粘贴：只允许完整单条码，最多4096字符，不能含换行、Tab或控制字符。请清空后重新输入。"; }
        });
    }
    sealed record InputBasis(Guid Runtime, long Configuration, string? Observation, string? Operation);
    InputBasis? inputBasis;
    SubmitParcelBarcode? submitted;
    bool submitting, overflow, editing, updatingDisplay;
    ParcelBarcodeInputKind inputKind = ParcelBarcodeInputKind.Keyboard;
    void PositionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.Barcode) or nameof(SupplyStationPositionViewModel.HasCurrentInput))
        {
            RefreshBarcodeDisplay();
            if (editing && InputFeedback.Text.StartsWith("输入尚未提交", StringComparison.Ordinal)) InputFeedback.Text = DraftNotice;
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
    void Entry_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => PrepareResultReplacement();
    void KeyboardSource_Click(object sender, RoutedEventArgs e) => SetInputKind(ParcelBarcodeInputKind.Keyboard);
    void PdaSource_Click(object sender, RoutedEventArgs e) => SetInputKind(ParcelBarcodeInputKind.PdaKeyboard);
    void SetInputKind(ParcelBarcodeInputKind value)
    {
        if (submitting || submitted is not null) return;
        inputKind = value;
        KeyboardSource.IsChecked = value == ParcelBarcodeInputKind.Keyboard;
        PdaSource.IsChecked = value == ParcelBarcodeInputKind.PdaKeyboard;
    }
    void CaptureInputBasis()
    {
        if (Host?.ViewModel is not { Fresh: true, Snapshot: { } snapshot } || DataContext is not SupplyStationPositionViewModel p) return;
        inputBasis = new(snapshot.RuntimeId, snapshot.Configuration.Revision, p.Input?.Observation.Id, p.Operation?.Id);
    }
    string DraftNotice => DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p
        ? "输入尚未提交；下方任务对应已读取条码：" + p.Barcode : "输入尚未提交。";
    void InputChanged(object sender, TextChangedEventArgs e)
    {
        if (BarcodeEntry is null || updatingDisplay || submitting || submitted is not null) return;
        editing = true;
        InputFeedback.Text = DraftNotice;
        if (BarcodeEntry.Text.Length == 0) { inputBasis = null; overflow = false; }
        else if (inputBasis is null) CaptureInputBasis();
    }
    void RebaseInput_Click(object sender, RoutedEventArgs e)
    {
        if (submitting || submitted is not null) { InputFeedback.Text = "先核对本次提交结果，不可创建第二命令。"; return; }
        CaptureInputBasis(); RebaseInput.Visibility = Visibility.Collapsed; InputFeedback.Text = "已更新输入基准，请确认条码属于当前位正在处理的包裹。"; BarcodeEntry.Focus();
    }
    void ClearInput_Click(object sender, RoutedEventArgs e)
    {
        if (submitting || submitted is not null) { InputFeedback.Text = "先核对本次提交结果，不能通过清空输入解除原责任。"; return; }
        editing = true; BarcodeEntry.Clear(); overflow = false; inputBasis = null; InputFeedback.Text = "已清空本窗输入，当前作业保持。"; BarcodeEntry.Focus();
    }
    async void BarcodeEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true; if (!e.IsRepeat) await SubmitBarcodeAsync();
    }
    void Entry_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (submitting || submitted is not null)
        { e.Handled = true; InputFeedback.Text = "正在提交或核对原输入；后续扫码未接纳，请取得结果后重扫。"; return; }
        PrepareResultReplacement();
        if (BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + e.Text.Length > 4096 || e.Text.Any(char.IsControl))
        { e.Handled = true; overflow = true; InputFeedback.Text = "输入超过预算或含控制字符，不能提交截断码；请清空后重新输入完整单条码。"; }
    }
    async void SubmitBarcode_Click(object sender, RoutedEventArgs e) => await SubmitBarcodeAsync();
    async Task SubmitBarcodeAsync()
    {
        if (submitting || Host is not { } host || DataContext is not SupplyStationPositionViewModel p) return;
        var vm = host.ViewModel;
        if (!vm.CanStartOperation || !vm.Fresh || vm.Application is not IParcelWorkstation service)
        { InputFeedback.Text = "当前作业输入不可用，请先恢复有效状态。"; return; }
        if (submitted is null)
        {
            if (!editing) { InputFeedback.Text = "当前显示的是已读取条码；输入本次包裹条码后再提交。"; return; }
            if (overflow) { InputFeedback.Text = "输入不完整，请清空后重扫或重新输入。"; return; }
            if (BarcodeEntry.Text.Length == 0) { InputFeedback.Text = "请输入完整条码，或将PDA设为键盘输入并以回车结束。"; return; }
            if (inputBasis is null) CaptureInputBasis();
            if (inputBasis is not { } basis) { InputFeedback.Text = "输入基准未取得，请先核对当前位。"; return; }
            submitted = new(Wire.Id(), basis.Runtime, basis.Configuration, p.PositionId, BarcodeEntry.Text,
                inputKind, basis.Observation, basis.Operation);
        }
        var request = submitted; submitting = true; BarcodeEntry.IsReadOnly = true; KeyboardSource.IsEnabled = PdaSource.IsEnabled = false;
        SubmitBarcode.IsEnabled = false; RebaseInput.IsEnabled = false;
        InputFeedback.Text = "正在提交本次条码；请等待结果，不要重复扫描。";
        await vm.RunAsync(async () =>
        {
            try
            {
                var receipt = await vm.ServiceAsync(() => service.SubmitParcelBarcodeAsync(request, CancellationToken.None));
                var label = receipt.Status switch { "Admitted" => "已准入", "Rejected" => "未准入", "Blocked" => "已阻断", "Conflict" => "需重新核对", "Duplicate" => "重复输入", "Pending" => "结果待核对", _ => receipt.Status };
                InputFeedback.Text = label + " · " + receipt.Detail + (receipt.OperationId is null ? "" : "\n原作业：" + receipt.OperationId) +
                    (receipt.Status == "Pending" ? "\n命令：" + request.CommandId : "");
                vm.Message = InputFeedback.Text;
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
                InputFeedback.Text = "提交结果未确认，请核对本次提交或从记录检查原作业，不自动重发。命令：" + request.CommandId + "\n" + error.Message;
                throw;
            }
        }, "提交或核对本位人工条码");
        submitting = false; BarcodeEntry.IsReadOnly = submitted is not null; KeyboardSource.IsEnabled = PdaSource.IsEnabled = submitted is null;
        SubmitBarcode.Content = submitted is null ? "提交" : "核对本次提交";
        SubmitBarcode.IsEnabled = true; RebaseInput.IsEnabled = submitted is null; RefreshBarcodeDisplay();
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
