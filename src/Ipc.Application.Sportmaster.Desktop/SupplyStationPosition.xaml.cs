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
    public SupplyStationPosition() : this(null) { }

    internal SupplyStationPosition(ToteStationLaneControl? tote)
    {
        InitializeComponent();
        if (tote is not null) UseToteLayout(tote);
        InitializeCompositionFeedback();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (DataContext is not SupplyStationPositionViewModel p) return;
            p.SetInputFocused(true);
            // Buttons and selectable values keep their native mouse behaviour.
            // The rest of the card immediately becomes a barcode input surface.
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != this;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is ButtonBase or TextBoxBase or ComboBox or ScrollBar) return;
            FocusBarcodeInput();
            // ScrollViewer's default mouse handling would otherwise take focus
            // back from the barcode editor after this preview event.
            e.Handled = true;
        };
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(IsKeyboardFocusWithin);
        };
        DataContextChanged += (_, e) =>
        {
            presentationGeneration++; feedbackShown = readMeasured = slotMeasured = rejectionShown = successShown = null;
            ResetPdaDetection();
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= PositionChanged;
            if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += PositionChanged;
            RefreshBarcodeDisplay(); RefreshSubmitAvailability();
        };
        Loaded += (_, _) => RefreshSubmitAvailability();
        Unloaded += (_, _) => { if (DataContext is SupplyStationPositionViewModel p) p.SetInputFocused(false); };
        BarcodeEntry.LostKeyboardFocus += (_, _) => ResetPdaDetection();
        BarcodeEntry.PreviewMouseDown += (_, _) => ResetPdaDetection();
        DataObject.AddPastingHandler(BarcodeEntry, (_, e) =>
        {
            ResetPdaDetection();
            PrepareResultReplacement();
            var value = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (submitting || submitted is not null || value is null || value.Any(char.IsControl) ||
                BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + value.Length > 4096)
            { e.CancelCommand(); overflow = true; showingDraftNotice = false; RefreshSubmitAvailability(); InputFeedback.Text = UiText.Get("Tote.InvalidPaste"); }
        });
    }
    void UseToteLayout(ToteStationLaneControl tote)
    {
        // The alternate layout uses the exact same barcode editor, guarded
        // submission/recheck actions, and Unknown guidance as the classic view.
        static void Move(FrameworkElement element, ContentControl host)
        {
            ((Panel)element.Parent).Children.Remove(element);
            host.Content = element;
        }
        Move(BarcodeEntry, tote.ScanEntry);
        Move(SubmitBarcode, tote.DispatchAction);
        Move(RebaseInput, tote.ConfirmInput);
        Move(ConfirmManualPlacementRemoval, tote.ParcelRemoval);
        Move(InputFeedback, tote.Feedback);
        Move(RcsGuidancePanel, tote.UnknownGuide);
        SubmitBarcode.SetResourceReference(ContentControl.ContentProperty, "Tote.Dispatch");
        SubmitBarcode.Margin = new(0, 0, 5, 0);
        BarcodeEntry.FontSize = UiTypography.Body;
        BarcodeEntry.MinHeight = 44;
        tote.ClearButton.Click += ClearInput_Click;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SupplyStationPositionViewModel p)
                System.Windows.Automation.AutomationProperties.SetAutomationId(BarcodeEntry,
                    p.PositionId == "left" ? "Tote.Left.BarcodeInput" : "Tote.Right.BarcodeInput");
        };
        Content = tote;
        Background = null;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && IsBarcodeInputFocused && editing) args.Handled = ClearToteDraft();
        };
    }

    internal bool ClearToteDraft()
    {
        if (Content is not ToteStationLaneControl || !editing) return false;
        ClearInput_Click(this, new RoutedEventArgs());
        return true;
    }

    sealed record InputBasis(Guid Runtime, long Configuration, string? Observation, string? Operation, string? Round, string? Arrival, string? Placement);
    const int PdaMinimumLength = 6, PdaRapidGapMs = 35, PdaMaximumGapMs = 250, PdaEnterGapMs = 100;
    (int At, int Length, char Character)? expectedKeyboardText;
    int? lastKeyboardCharacterAt;
    int rapidCharacterGaps;
    InputBasis? inputBasis;
    SubmitParcelBarcode? submitted;
    bool submitting, overflow, editing, updatingDisplay, showingDraftNotice;
    bool terminalTextRetained, completedInputBurst;
    string? terminalObservationKey;
    void PositionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.Arrival) && inputBasis is { } basis &&
            DataContext is SupplyStationPositionViewModel current && (basis.Arrival != current.Arrival?.Generation || basis.Placement != current.Arrival?.PlacementBoundary))
        {
            ResetPdaDetection();
            if (editing && submitted is null)
            {
                RebaseInput.Visibility = Visibility.Visible;
                InputFeedback.Text = UiText.Get("Tote.VehicleChanged");
            }
        }
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.ShowCenteredState) or nameof(SupplyStationPositionViewModel.CenteredAttention)) RefreshDraftNoticeVisibility();
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.SupplyEnabled))
        {
            if (DataContext is not SupplyStationPositionViewModel { SupplyEnabled: true }) ResetPdaDetection();
            RefreshSubmitAvailability();
            Host?.RefreshEnabledPositionFocus();
        }
        if (e.PropertyName == nameof(SupplyStationPositionViewModel.CanSubmitBarcode)) RefreshSubmitAvailability();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.CanViewCurrentPicture) or nameof(SupplyStationPositionViewModel.PictureUnavailableReason)) RefreshPictureAvailability();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.SuccessIdentity) or nameof(SupplyStationPositionViewModel.ReadFeedbackKey) or nameof(SupplyStationPositionViewModel.RejectionIdentity) or nameof(SupplyStationPositionViewModel.ShowConfirmedTask)) ObservePresentation();
        if (e.PropertyName is nameof(SupplyStationPositionViewModel.Barcode) or nameof(SupplyStationPositionViewModel.HasCurrentInput) or nameof(SupplyStationPositionViewModel.BarcodeObservationKey))
        {
            RefreshBarcodeDisplay();
            if (editing && showingDraftNotice) InputFeedback.Text = DraftNotice;
            RefreshDraftNoticeVisibility();
        }
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
        if (completedInputBurst && !submitting && submitted is null)
        {
            completedInputBurst = false; editing = false; inputBasis = null; ResetPdaDetection();
            BarcodeEntry.SelectAll();
        }
        else if (!editing && !submitting && submitted is null) BarcodeEntry.SelectAll();
    }
    internal void FocusBarcodeInput()
    {
        if (!IsVisible || !IsEnabled || DataContext is not SupplyStationPositionViewModel { SupplyEnabled: true }) return;
        BarcodeEntry.Focus(); PrepareResultReplacement();
    }
    internal bool IsBarcodeInputFocused => BarcodeEntry.IsKeyboardFocused;
    internal void ReceiveScannerText(string text, int? timestamp)
    {
        FocusBarcodeInput();
        if (submitting || submitted is not null || BarcodeEntry.IsReadOnly) return;
        if (BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + text.Length > 4096)
        { overflow = true; InputFeedback.Text = "条码超长，请清空后重扫。"; RefreshSubmitAvailability(); return; }
        ExpectKeyboardText(text, timestamp);
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
        BarcodeEntry.IsReadOnly = submitting || submitted is not null || DataContext is not SupplyStationPositionViewModel { SupplyEnabled: true };
        BarcodeEntry.IsTabStop = DataContext is SupplyStationPositionViewModel { SupplyEnabled: true };
        if (submitted is not null) available = Host?.ViewModel is { CanUseWorkspace: true, CanStartOperation: true };
        SubmitBarcode.IsEnabled = !submitting && available && (submitted is not null || editing && !overflow && BarcodeEntry.Text.Length > 0);
        RefreshPictureAvailability();
        RefreshDraftNoticeVisibility();
    }
    void RefreshDraftNoticeVisibility()
    {
        if (InputFeedback is null) return;
        if (showingDraftNotice && InputFeedback.Text == DraftNotice &&
            DataContext is SupplyStationPositionViewModel { ShowCenteredState: true, CenteredAttention: false })
            InputFeedback.Visibility = Visibility.Collapsed;
        else InputFeedback.ClearValue(VisibilityProperty);
    }
    void RefreshPictureAvailability()
    {
        if (CurrentBarcode is null || DataContext is not SupplyStationPositionViewModel position) return;
        var reason = editing ? "Editing" : terminalTextRetained ? "ManualTextRetained" :
            submitting || submitted is not null ? "ManualSubmissionPending" : position.PictureUnavailableReason;
        CurrentBarcode.IsEnabled = reason == "Available";
        if (Host?.ViewModel is { Snapshot: { } snapshot } vm && vm.Application is Ipc.Application.Contracts.IParcelPresentationDiagnostics diagnostics)
            diagnostics.ReportPicturePresentation(new(snapshot.RuntimeId, position.PositionId, snapshot.Revision,
                position.Input?.Observation.Id, position.ReadFeedbackIdentity, position.CurrentPictureQuery?.ObservationId,
                CurrentBarcode.IsEnabled, reason));
    }
    void CaptureInputBasis()
    {
        if (Host?.ViewModel is not { Fresh: true, Snapshot: { } snapshot } || DataContext is not SupplyStationPositionViewModel p) return;
        inputBasis = new(snapshot.RuntimeId, snapshot.Configuration.Revision, p.Input?.Observation.Id, p.Operation?.Id, p.CurrentRound?.Id, p.Arrival?.Generation, p.Arrival?.PlacementBoundary);
    }
    string DraftNotice => DataContext is SupplyStationPositionViewModel { HasCurrentInput: true } p
        ? p.ShowConfirmedTask ? "输入未提交；下方格口属于 " + p.Barcode + "。" : "输入未提交；本位当前读码：" + (p.Barcode.Length == 0 ? "多个或无效条码" : p.Barcode) + "。"
        : "输入未提交。";
    void InputChanged(object sender, TextChangedEventArgs e)
    {
        if (BarcodeEntry is null || updatingDisplay || submitting || submitted is not null) return;
        // Track only text actually inserted by this key event. Paste, edits and
        // programmatic changes keep the draft on the ordinary keyboard path.
        var text = BarcodeEntry.Text;
        if (expectedKeyboardText is { } key && text.Length == key.Length && text[^1] == key.Character)
        {
            expectedKeyboardText = null;
            if (text.Length == 1) { lastKeyboardCharacterAt = key.At; rapidCharacterGaps = 0; }
            else if (lastKeyboardCharacterAt is { } previous && WithinInputGap(key.At, previous, PdaMaximumGapMs))
            {
                if (WithinInputGap(key.At, previous, PdaRapidGapMs)) rapidCharacterGaps++;
                lastKeyboardCharacterAt = key.At;
            }
            else ResetPdaDetection();
        }
        else ResetPdaDetection();
        terminalTextRetained = false; RebaseInput.Visibility = Visibility.Collapsed;
        editing = BarcodeEntry.Text.Length > 0;
        showingDraftNotice = editing; InputFeedback.Text = editing ? DraftNotice : "输入已清空，相机读码将自动更新。";
        if (!editing)
        {
            inputBasis = null; overflow = false;
            // Same-placement camera observations deliberately create no new input.
            // Resume the existing camera projection after TextChanged finishes.
            if (DataContext is SupplyStationPositionViewModel { Input.Observation.OperatorInput: null })
                Dispatcher.BeginInvoke(new Action(RefreshBarcodeDisplay));
        }
        else if (inputBasis is null) CaptureInputBasis();
        RefreshSubmitAvailability();
    }
    void RebaseInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null) { InputFeedback.Text = "上次提交结果未确认，请先点“查看提交结果”。"; return; }
        ResetPdaDetection();
        terminalTextRetained = false; editing = BarcodeEntry.Text.Length > 0;
        CaptureInputBasis(); RebaseInput.Visibility = Visibility.Collapsed; InputFeedback.Text = UiText.Get("Tote.Rebased"); BarcodeEntry.Focus(); RefreshSubmitAvailability();
    }
    void ClearInput_Click(object sender, RoutedEventArgs e)
    {
        showingDraftNotice = false;
        if (submitting || submitted is not null)
        {
            InputFeedback.Text = Content is ToteStationLaneControl ? UiText.Get("Tote.ClearBlocked") : "上次提交结果未确认，暂不能清空；请先点“查看提交结果”。";
            return;
        }
        if (Content is ToteStationLaneControl && !editing) return;
        ResetPdaDetection();
        terminalTextRetained = false; editing = false; BarcodeEntry.Clear(); overflow = false; inputBasis = null;
        InputFeedback.Text = UiText.Get("Tote.ClearFeedback"); BarcodeEntry.Focus(); RefreshSubmitAvailability();
    }
    async void BarcodeEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            ResetPdaDetection();
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true; if (!e.IsRepeat) await SubmitBarcodeAsync(Host?.TakeBarcodeEnterTimestamp() ?? e.Timestamp);
    }
    void Entry_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (submitting || submitted is not null)
        { e.Handled = true; InputFeedback.Text = "上一次提交还在处理，本次扫描未接收；结果出来后请重扫。"; return; }
        PrepareResultReplacement();
        if (BarcodeEntry.Text.Length - BarcodeEntry.SelectionLength + e.Text.Length > 4096 || e.Text.Any(char.IsControl))
        { e.Handled = true; overflow = true; showingDraftNotice = false; RefreshSubmitAvailability(); InputFeedback.Text = "条码过长或含无效字符，已停止接收。请清空后重新输入完整条码。"; }
        else ExpectKeyboardText(e.Text, Host?.TakeBarcodeKeyTimestamp());
    }
    void ExpectKeyboardText(string text, int? timestamp)
    {
        // Keyboard-wedge scanners use the same Windows text channel as typing.
        // Only a complete, rapid sequence followed by Enter is classified as PDA.
        var length = BarcodeEntry.Text.Length;
        expectedKeyboardText = timestamp is not { } at || text.Length != 1 ? null :
            BarcodeEntry.SelectionLength == length ? (at, 1, text[0]) :
            BarcodeEntry.SelectionLength == 0 && BarcodeEntry.SelectionStart == length ? (at, length + 1, text[0]) : null;
    }
    void ResetPdaDetection() { expectedKeyboardText = null; lastKeyboardCharacterAt = null; rapidCharacterGaps = 0; }
    static bool WithinInputGap(int later, int earlier, uint maximumMs) => unchecked((uint)(later - earlier)) <= maximumMs;
    async void SubmitBarcode_Click(object sender, RoutedEventArgs e) => await SubmitBarcodeAsync();
    async void EnableSupply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SupplyStationPositionViewModel p)
        {
            await p.SetSupplyEnabledAsync(true);
            if (Window.GetWindow(this)?.IsActive == true) FocusBarcodeInput();
        }
    }
    async void DisableSupply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SupplyStationPositionViewModel p) await p.SetSupplyEnabledAsync(false);
    }
    async void ConfirmPlacementRemoval_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SupplyStationPositionViewModel p) await p.ConfirmPlacementRemovalAsync();
    }
    async Task SubmitBarcodeAsync(int? enterTimestamp = null)
    {
        showingDraftNotice = false;
        RefreshDraftNoticeVisibility();
        if (submitting || Host is not { } host || DataContext is not SupplyStationPositionViewModel p) return;
        var vm = host.ViewModel;
        if (!vm.CanStartOperation || !vm.Fresh || vm.Application is not IParcelWorkstation service)
        { InputFeedback.Text = UiText.Get("Tote.SubmitWait"); return; }
        if (submitted is null)
        {
            if (!p.CanSubmitBarcode) { InputFeedback.Text = UiText.Get("Tote.LaneBlocked"); RefreshSubmitAvailability(); return; }
            if (!editing) { InputFeedback.Text = UiText.Get("Tote.OldBarcode"); return; }
            if (overflow) { InputFeedback.Text = UiText.Get("Tote.IncompleteBarcode"); return; }
            if (BarcodeEntry.Text.Length == 0) { InputFeedback.Text = UiText.Get("Tote.EnterFullBarcode"); return; }
            if (inputBasis is null) CaptureInputBasis();
            if (inputBasis?.Arrival != p.Arrival?.Generation || inputBasis?.Placement != p.Arrival?.PlacementBoundary)
            {
                ResetPdaDetection(); RebaseInput.Visibility = Visibility.Visible;
                InputFeedback.Text = UiText.Get("Tote.VehicleRecheck");
                return;
            }
            if (inputBasis is not { } basis) { InputFeedback.Text = UiText.Get("Tote.NoInputBasis"); return; }
            // A burst is predominantly rapid. A few delayed key messages must
            // not change its source, but a break or an edited draft must.
            var length = BarcodeEntry.Text.Length;
            var kind = enterTimestamp is { } entered && lastKeyboardCharacterAt is { } last && length >= PdaMinimumLength &&
                rapidCharacterGaps >= length / 2 && WithinInputGap(entered, last, PdaEnterGapMs)
                ? ParcelBarcodeInputKind.PdaKeyboard : ParcelBarcodeInputKind.Keyboard;
            submitted = new(Wire.Id(), basis.Runtime, basis.Configuration, p.PositionId, BarcodeEntry.Text, kind, basis.Observation, basis.Operation) { ExpectedRoundId = basis.Round, ExpectedArrivalGeneration = basis.Arrival, ExpectedPlacementBoundary = basis.Placement };
            ResetPdaDetection();
        }
        var request = submitted; submitting = true; BarcodeEntry.IsReadOnly = true;
        SubmitBarcode.IsEnabled = false; RebaseInput.IsEnabled = false;
        InputFeedback.Text = UiText.Get("Tote.Submitting");
        // Submission ownership is local to this position; management Busy remains global.
            try
            {
                var receipt = await vm.ServiceAsync(() => service.SubmitParcelBarcodeAsync(request, CancellationToken.None));
                InputFeedback.Text = receipt.Status switch
                {
                    "Admitted" => UiText.Get("Tote.Submitted"),
                    "Rejected" => UiText.Get("Tote.Refused"),
                    "Blocked" => UiText.Get("Tote.Blocked"),
                    "Conflict" => UiText.Get("Tote.LaneChanged"),
                    "Duplicate" => UiText.Get("Tote.Duplicate"),
                    _ => SubmissionUnconfirmed
                };
                vm.ReportBarcodeFeedback(receipt, InputFeedback.Text);
                if (receipt.Status is "Admitted" or "Rejected" or "Blocked" or "Conflict" or "Duplicate")
                {
                    completedInputBurst = true;
                    submitted = null;
                    editing = false; inputBasis = null;
                    terminalTextRetained = true;
                    terminalObservationKey = p.BarcodeObservationKey;
                    PrepareResultReplacement();
                    if (receipt.Status == "Conflict") RebaseInput.Visibility = Visibility.Visible;
                    if (receipt.Status == "Admitted") { editing = false; inputBasis = null; InputFeedback.Text = ""; }
                }
            }
            catch (Exception)
            {
                // The Application journals the original exception and command.
                // Exception type alone cannot prove that no intent was committed.
                InputFeedback.Text = SubmissionUnconfirmed;
                vm.Message = InputFeedback.Text;
            }
        try { await vm.RefreshAsync(); }
        catch (Exception error) { vm.MarkReadFailure("提交后的状态读取失败：" + error.Message); }
        finally
        {
        submitting = false; BarcodeEntry.IsReadOnly = submitted is not null;
        SubmitBarcode.SetResourceReference(ContentControl.ContentProperty,
            submitted is not null ? "Tote.CheckSubmission" : Content is ToteStationLaneControl ? "Tote.Dispatch" : "Work.Submit");
        RefreshSubmitAvailability(); RebaseInput.IsEnabled = submitted is null; RefreshBarcodeDisplay(); PrepareResultReplacement();
        }
    }
    static string SubmissionUnconfirmed => UiText.Get("Tote.UnconfirmedSubmit");
    void Picture_Click(object sender, RoutedEventArgs e)
    {
        if (editing || terminalTextRetained || submitting || submitted is not null || Host is not { } host ||
            DataContext is not SupplyStationPositionViewModel { CurrentPictureQuery: { } query } p) return;
        Ipc.Application.ParcelFlow.Desktop.ParcelPictureWindow.Open(host, host.ViewModel, query, p.Barcode);
    }
    SupplyStationWindow? Host => Window.GetWindow(this) as SupplyStationWindow;
    void New_Click(object sender, RoutedEventArgs e) { if (DataContext is SupplyStationPositionViewModel p) p.NewParcel(); }
    async void Emit_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.EmitPositionAsync(p); }
    async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } host && DataContext is SupplyStationPositionViewModel { WorkDetailsOperationId: { } operationId })
            await host.ShowDetailsAsync(operationId);
    }
    async void Reconcile_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ReconcilePositionAsync(p); }
    async void Continue_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ContinuePositionAsync(p); }
}

/// <summary>Keeps a multiline RCS response selectable in a viewport sized to whole text lines.</summary>
public sealed class RejectionMessageBox : TextBox
{
    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        var line = FontFamily.LineSpacing * FontSize;
        var lines = Math.Floor(arrangeBounds.Height / line);
        if (lines >= 1)
            arrangeBounds.Height = lines * line;
        return base.ArrangeOverride(arrangeBounds);
    }
}
