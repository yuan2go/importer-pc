using Ipc.Presentation.Wpf;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// A projection of the Application's one-shot round, never a local camera/sender.
internal sealed partial class StationDebugControl : UserControl
{
    static string DebugFailure => UiText.Get("Debug.Incomplete");
    readonly SupplyStationViewModel vm;
    readonly ICameraInspection? inspection;
    readonly ICameraReadRounds? rounds;
    readonly StationDebugEvidenceControl evidence;
    readonly StationCameraPreviewControl livePreview;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    static readonly UTF8Encoding responseEncoding = new(false, true);
    static readonly JsonSerializerOptions responseFormat = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
    IntegrationHttpResponse? displayedResponse;
    string? originalResponseText;
    CancellationTokenSource? lifetime;
    CameraInspectionStatus? scope;
    CameraReadRound? current;
    CameraReadRound? observed;
    CameraFrameMetadata? displayedFrame;
    BeginCameraInspection? beginCommand;
    ArmCameraRead? armCommand;
    SendCameraRound? sendCommand;
    PrepareManualRcsRequest? prepareCommand;
    readonly Dictionary<string, string> manualDrafts = [];
    bool updatingRequest, requestDirty;
    string position = "left", loadedFrame = "";
    string? ownedScanSession, ownedScanRound, ownedManualRound;
    Guid? ownedManualRuntime;
    bool active, busy, polling, fit = true, authoritative, stationDebug = true;
    long generation;
    TaskCompletionSource? actionFinished;
    Task deactivation = Task.CompletedTask;
    long activation;
    double zoom = 1;
    Point? pan;
    double panX, panY;
    SupplyStationPositionViewModel Position => position == "left" ? vm.Left : vm.Right;
    ICameraInspection Inspection => inspection ?? throw new NotSupportedException(UiText.Get("Debug.CameraUnsupported"));
    ICameraReadRounds Rounds => rounds ?? throw new NotSupportedException(UiText.Get("Debug.ScanUnsupported"));
    public StationDebugControl(SupplyStationViewModel vm, Func<string, Task> settings, Func<string, Task> details)
    {
        InitializeComponent();
        this.vm = vm;
        inspection = vm.Application as ICameraInspection;
        rounds = vm.Application as ICameraReadRounds;
        evidence = new(vm, settings, details);
        livePreview = new(vm, previewHeight: 206, showDiagnostics: false);
        livePreview.SelectDevice(Position.ReaderId);
        previewHost.Content = livePreview;
        previewDiagnostics.Content = livePreview.CreateDiagnostics();
        evidenceHost.Content = evidence;
        InitializeBatch(batchPanel);
        calibrate.Click += async (_, _) => await Run(_ => livePreview.HasLiveFrame
            ? vm.CalibrateEmptyTrayAsync(Position, stationDebug)
            : throw new InvalidOperationException(UiText.Get("Debug.AwaitLive")));
        previewTabs.SelectionChanged += (_, args) => { if (args.Source == previewTabs) Buttons(); };

        stopRead.Click += async (_, _) => await Run(async _ =>
        {
            if (ownedScanSession is not { } session || ownedScanRound is not { } round) return;
            await CloseScanScopeAsync(session, round);
            feedback.Text = UiText.Get("Debug.ScanStopped");
        });
        adapt.Click += (_, _) => { fit = true; ApplyZoom(); }; actual.Click += (_, _) => { fit = false; zoom = 1 / VisualTreeHelper.GetDpi(viewport).DpiScaleX; ApplyZoom(); };
        more.Click += (_, _) => Scale(1.25); less.Click += (_, _) => Scale(.8); overlay.Checked += (_, _) => DrawMarks(); overlay.Unchecked += (_, _) => DrawMarks();
        viewport.SizeChanged += (_, _) => { if (fit) ApplyZoom(); };
        viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; Scale(e.Delta > 0 ? 1.25 : .8); e.Handled = true; };
        viewport.PreviewMouseLeftButtonDown += (_, e) => { if (displayedFrame is null || e.OriginalSource is System.Windows.Controls.Primitives.Thumb) return; pan = e.GetPosition(viewport); panX = viewport.HorizontalOffset; panY = viewport.VerticalOffset; viewport.CaptureMouse(); e.Handled = true; };
        viewport.MouseMove += (_, e) => { if (pan is not { } point || !viewport.IsMouseCaptured) return; var now = e.GetPosition(viewport); viewport.ScrollToHorizontalOffset(panX + point.X - now.X); viewport.ScrollToVerticalOffset(panY + point.Y - now.Y); };
        viewport.MouseLeftButtonUp += (_, _) => { pan = null; viewport.ReleaseMouseCapture(); };
        SizeChanged += (_, _) => LayoutColumns();
        debugScroll.ScrollChanged += (_, args) =>
        {
            if (args.ViewportHeightChange != 0) FitRequestCard();
        };
        foreach (var row in requestPanel.Children.OfType<FrameworkElement>().Where(child => child != requestBody && child != responseBody))
            row.SizeChanged += (_, _) => FitRequestCard();
        left.Click += (_, _) => SelectPosition("left"); right.Click += (_, _) => SelectPosition("right");
        trigger.Click += async (_, _) => await Run(TriggerAsync); send.Click += async (_, _) => await Run(SendAsync);
        prepare.Click += async (_, _) => await Run(PrepareManualAsync);
        copyResponse.Click += async (_, _) => await CopyResponseAsync();
        requestBarcode.TextChanged += (_, _) =>
        {
            if (updatingRequest) return;
            requestDirty = true; manualDrafts[position] = requestBarcode.Text; Buttons();
        };
        timer.Tick += async (_, _) => { Buttons(); await PollAsync(); };
        Loaded += async (_, _) =>
        {
            var requested = ++activation; await deactivation;
            if (!IsLoaded || requested != activation) return;
            active = true; lifetime = new(); timer.Start(); LayoutColumns(); Buttons(); await PollAsync();
        };
        Unloaded += (_, _) => Deactivate();
    }
    public bool ToggleEvidence()
    {
        tabs.SelectedIndex = tabs.SelectedIndex == 0 ? 1 : 0;
        return tabs.SelectedIndex == 1;
    }

    async Task PrepareManualAsync(CancellationToken token)
    {
        if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException(UiText.Get("Debug.RestoreStatus"));
        if (prepareCommand is { } pending && (pending.PositionId != position || pending.RuntimeId != snapshot.RuntimeId))
            throw new InvalidOperationException(UiText.Get("Debug.CheckOriginalLane"));
        var command = prepareCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, position, requestBarcode.Text, observed?.Id)
        { ExpectedArrivalGeneration = snapshot.Positions?.SingleOrDefault(p => p.Id == position)?.Arrival?.Generation };
        var owner = lifetime; var version = generation; var scanSession = ownedScanSession;
        CameraReadRound value;
        try { value = await vm.ServiceAsync(() => Rounds.PrepareManualRcsRequestAsync(command, token)); }
        catch (CameraReadRoundConflictException) { if (ReferenceEquals(prepareCommand, command)) prepareCommand = null; throw; }
        catch (ArgumentException) { if (ReferenceEquals(prepareCommand, command)) prepareCommand = null; throw; }
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode || !ReferenceEquals(owner, lifetime) || version != generation)
        {
            // UI publication and durable cleanup have different lifetimes. Even
            // if this response projects a newer round, only our command is ours.
            await CloseOwnedManualAsync(command.CommandId, command.RuntimeId);
            if (scanSession is not null && value.SessionId == scanSession)
                await CloseOwnedScanAsync(scanSession, command.CommandId);
            if (ReferenceEquals(prepareCommand, command)) prepareCommand = null;
            return;
        }
        if (value.Id != command.CommandId || !value.ManualRequest) throw new InvalidOperationException(UiText.Get("Debug.CheckPrepare"));
        requestDirty = requestBarcode.Text != command.Barcode;
        if (!requestDirty) manualDrafts.Remove(position);
        prepareCommand = null; sendCommand = null;
        ownedManualRound = value.Id;
        ownedManualRuntime = command.RuntimeId;
        if (scope is { SingleReadRounds: true } && value.SessionId == scope.SessionId)
        { ownedScanSession = value.SessionId; ownedScanRound = value.Id; }
        current = observed = value; authoritative = true; feedback.Text = ""; await PublishRoundAsync(value, token);
    }
    async Task TriggerAsync(CancellationToken token)
    {
        if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException(UiText.Get("Debug.RestoreStatus"));
        generation++; ClearRoundDisplay();
        if (scope is not { State: "Active", SingleReadRounds: true })
        {
            if (scope is { SingleReadRounds: true, SessionId: { } oldSession })
                await CloseScanScopeAsync(oldSession, observed?.Id);
            beginCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.ObserveProduction, "单次扫码调试", new()) { SingleReadRounds = true };
            var entered = await vm.ServiceAsync(() => Inspection.BeginCameraInspectionAsync(beginCommand, token));
            ownedScanSession = entered.SessionId; ownedScanRound = observed?.Id;
            if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
            scope = entered;
            beginCommand = null;
            // The begin receipt is followed by an authoritative status read, including the round mode.
            var confirmed = await vm.ServiceAsync(() => Inspection.CameraInspectionStatusAsync(token));
            if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
            scope = confirmed;
        }
        if (scope?.SessionId is not { } session) throw new InvalidOperationException(UiText.Get("Debug.NoSession"));
        beginCommand = null; // An authoritative active scope settles a lost begin receipt.
        armCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, session, Position.ReaderId, observed?.Id ?? current?.Id);
        if (armCommand.DeviceId != Position.ReaderId || armCommand.SessionId != session || armCommand.RuntimeId != snapshot.RuntimeId)
            throw new InvalidOperationException(UiText.Get("Debug.WrongCamera"));
        CameraReadRound value;
        try { value = await vm.ServiceAsync(() => Rounds.ArmCameraReadAsync(armCommand, token)); }
        catch (CameraReadRoundConflictException) { armCommand = null; throw; }
        ownedScanSession = armCommand.SessionId; ownedScanRound = armCommand.CommandId;
        if (value.Id != armCommand.CommandId) throw new CameraReadRoundConflictException(UiText.Get("Debug.OtherRound"));
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
        armCommand = null; sendCommand = null; requestDirty = false; manualDrafts.Remove(position); observed = current = value;
        ownedScanSession = value.SessionId; ownedScanRound = value.Id;
        authoritative = true; feedback.Text = ""; await PublishRoundAsync(value, token);
    }
    async Task SendAsync(CancellationToken token)
    {
        if (!authoritative || !vm.Fresh || current is not { Id: { } id, RequestHash: { } hash } value || vm.Snapshot is not { } snapshot) throw new InvalidOperationException(UiText.Get("Debug.RestoreRound"));
        if (sendCommand is null && (requestDirty || !value.ManualRequest && loadedFrame != value.Frame?.Id)) throw new InvalidOperationException(UiText.Get("Debug.PrepareBarcode"));
        sendCommand ??= new(Wire.Id(), snapshot.RuntimeId, id, hash);
        if (sendCommand.RuntimeId != snapshot.RuntimeId || sendCommand.RoundId != id || sendCommand.RequestHash != hash)
            throw new InvalidOperationException(UiText.Get("Debug.NoOtherRoundSend"));
        var returned = await vm.ServiceAsync(() => Rounds.SendCameraRoundAsync(sendCommand, token));
        if (returned.Id == id) sendCommand = null;
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
        current = returned; authoritative = true; await PublishRoundAsync(current, token);
        if (returned.SendAttempts == 0)
        {
            sendCommand = null;
            feedback.Text = UiText.Get("Debug.NotSentPrefix") + (returned.SendBlockedReason ?? "尚未发送 HTTP 请求，请查看请求状态。");
            return;
        }
        if (returned.ManualRequest) { ownedManualRound = null; ownedManualRuntime = null; }
        if (scope is { SingleReadRounds: true } && returned.SessionId is { Length: > 0 } session && returned.Id is { } round)
            await CloseScanScopeAsync(session, round);
    }
    async Task Run(Func<CancellationToken, Task> action)
    {
        if (!active || busy || batch is not null) return;
        busy = true; var ownership = lifetime!; var token = ownership.Token;
        var completion = actionFinished = new(TaskCreationOptions.RunContinuationsAsynchronously); Buttons();
        var actionGeneration = generation;
        try { await vm.RequireManagementModeAsync(); if (!active || token.IsCancellationRequested || actionGeneration != generation || !vm.IsManagementMode) return; await action(token); }
        catch (OperationCanceledException) { if (active && ReferenceEquals(ownership, lifetime)) { authoritative = false; feedback.Text = UiText.Get("Debug.WaitStopped"); } }
        catch (Exception error) { if (active && ReferenceEquals(ownership, lifetime)) { authoritative = false; feedback.Text = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, DebugFailure); } }
        finally { busy = false; completion.TrySetResult(); if (active) { Buttons(); await PollAsync(); } }
    }
    public async Task PollAsync()
    {
        if (!stationDebug || !active || busy || batch is not null || polling || tabs.SelectedIndex == 1 || !vm.IsManagementMode || rounds is null || inspection is null) return;
        polling = true; var version = generation; var token = lifetime!.Token;
        try
        {
            var collection = await vm.ServiceAsync(() => Inspection.CameraInspectionStatusAsync(token));
            var value = await vm.ServiceAsync(() => Rounds.ReadCameraReadRoundAsync(token));
            if (!active || busy || version != generation || !vm.IsManagementMode) return;
            scope = collection; observed = value; authoritative = true;
            if (current?.Frame is not null && current.Id is not null && value.Id != current.Id)
            { state.Text = UiText.Get("Debug.AnotherRound"); Buttons(); return; }
            current = value; await PublishRoundAsync(value, token);
        }
        catch (OperationCanceledException) { if (active && version == generation) authoritative = false; }
        catch (Exception error) { if (active && version == generation) { authoritative = false; state.Text = UiText.Get("Debug.ReadFailure"); feedback.Text = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, DebugFailure); send.IsEnabled = false; } }
        finally { polling = false; if (active && version == generation) Buttons(); }
    }
    async Task PublishRoundAsync(CameraReadRound value, CancellationToken token)
    {
        if (!active || !vm.IsManagementMode || token.IsCancellationRequested) return;
        camera.Text = Position.Heading + " · " + Position.DeviceLabel + "    " + vm.RcsServiceStatus;
        var sameCamera = value.DeviceId == Position.ReaderId;
        var detail = !sameCamera ? UiText.Get("Debug.CameraNotTriggered") : value.State == "Faulted" ? UiText.Get("Debug.ScanIncomplete") :
            value.State == "Locked" && value.CanSend ? UiText.Get("Debug.ResultLockedWait") : Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(value.Detail, DebugFailure);
        state.ToolTip = detail;
        state.Text = value.Id is null || value.SendAttempts > 0 ? "" : detail;
        if (!sameCamera) { ClearRoundDisplay(); Buttons(); return; }
        SetText(barcode, value.Barcode ?? "");
        if (!requestDirty) { updatingRequest = true; try { SetText(requestBarcode, value.Barcode ?? ""); } finally { updatingRequest = false; } }
        SetText(requestBody, FormatJson(value.RequestBody ?? "")); ShowResponse(value);
        send.ToolTip = value.SendBlockedReason ?? (value.Target is null ? UiText.Get("Debug.PreparingRequest") : UiText.Get("Debug.SendToRcs") + value.Target);
        ToolTipService.SetShowOnDisabled(send, true);
        if (value.Frame is { ImageAvailable: true } frame && loadedFrame != frame.Id)
        {
            var version = generation; var raw = await vm.ServiceAsync(() => Inspection.ReadCameraOriginalAsync(frame.Id, token));
            if (!active || version != generation || token.IsCancellationRequested || current?.Id != value.Id || !vm.IsManagementMode) return;
            if (raw is null) { ClearPhoto(); photoCaption.Text = UiText.Get("Debug.PhotoOverwritten"); return; }
            if (raw.Metadata.Id != frame.Id || raw.Metadata.ObservationId != frame.ObservationId || raw.Gray8.LongLength != (long)frame.Width * frame.Height)
                throw new InvalidDataException(UiText.Get("Debug.ImageMismatch"));
            displayedFrame = raw.Metadata; loadedFrame = frame.Id;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, raw.Gray8, frame.Width); bitmap.Freeze(); photo.Source = bitmap; imageHint.Visibility = Visibility.Collapsed;
            photo.Width = imageLayer.Width = marks.Width = frame.Width; photo.Height = imageLayer.Height = marks.Height = frame.Height;
            lockedFrameTab.IsSelected = true; fit = true; ApplyZoom(); DrawMarks(); photoCaption.Text = UiText.Get("Debug.LockedFrame") + frame.FrameNumber + " · " + frame.CapturedAt.ToLocalTime().ToString("HH:mm:ss.fff") + UiText.Get("Debug.KeepImage");
        }
        if (value.Frame?.Id != loadedFrame || value.Frame?.ImageAvailable != true) ClearPhoto(); Buttons();
    }
    void ShowResponse(CameraReadRound value)
    {
        if (displayedResponse != value.HttpResponse)
        {
            displayedResponse = value.HttpResponse;
            originalResponseText = null;
            if (displayedResponse is { } response)
            {
                try { originalResponseText = responseEncoding.GetString(Convert.FromBase64String(response.BodyBase64)); }
                catch (FormatException) { /* Stored content cannot be shown as original text. */ }
                catch (DecoderFallbackException) { /* Do not silently replace invalid response bytes. */ }
            }
            var text = originalResponseText ?? "";
            SetText(responseBody, displayedResponse?.Complete == true ? FormatJson(text) : text);
        }
        result.Text = displayedResponse switch
        {
            { Complete: false } when string.IsNullOrEmpty(originalResponseText) => UiText.Get("Debug.NoBody"),
            { Complete: false } => UiText.Get("Debug.PartialBody"),
            not null when originalResponseText is null => UiText.Get("Debug.NotText"),
            not null when originalResponseText?.Length == 0 => UiText.Get("Debug.EmptyBody"),
            not null => UiText.Get("Debug.ReceivedResponse"),
            _ when value.SendBlockedReason is { } blocked => UiText.Get("Debug.NotSentDetail") + blocked,
            _ when value.State == "Sending" => UiText.Get("Debug.SendingStatus"),
            _ when value.SendAttempts > 0 => UiText.Get("Debug.MissingBody"),
            _ => "尚未发送请求"
        };
        result.Visibility = result.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    static string FormatJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        try { using var json = JsonDocument.Parse(text); return JsonSerializer.Serialize(json.RootElement, responseFormat); }
        catch (JsonException) { return text; }
    }

    async Task CopyResponseAsync()
    {
        if (!copyResponse.IsEnabled || originalResponseText is not { Length: > 0 } text) return;
        var version = generation;
        try
        {
            await vm.RequireManagementModeAsync();
            if (!active || version != generation || originalResponseText != text || displayedResponse?.Complete != true) return;
            Clipboard.SetText(text);
            feedback.Text = UiText.Get("Debug.CopiedResponse");
        }
        catch (System.Runtime.InteropServices.ExternalException) { feedback.Text = UiText.Get("Debug.Clipboard"); }
        catch (UnauthorizedAccessException) { feedback.Text = UiText.Get("Debug.CopyNeedsAuthorization"); }
        Buttons();
    }
    void ClearPhoto() { photo.Source = null; imageHint.Visibility = Visibility.Visible; marks.Children.Clear(); displayedFrame = null; loadedFrame = ""; imageLayer.Width = imageLayer.Height = 0; }
    void ClearRoundDisplay() { ClearPhoto(); barcode.Clear(); updatingRequest = true; try { SetText(requestBarcode, manualDrafts.GetValueOrDefault(position, "")); } finally { updatingRequest = false; } requestDirty = manualDrafts.ContainsKey(position); requestBody.Clear(); responseBody.Clear(); displayedResponse = null; originalResponseText = null; copyResponse.IsEnabled = false; send.ToolTip = state.ToolTip = null; result.Text = UiText.Get("Debug.NotSent"); result.Visibility = Visibility.Visible; photoCaption.Text = UiText.Get("Debug.WaitPhoto"); imageHint.Text = UiText.Get("Debug.WaitPhotoHint"); }
    void Buttons()
    {
        UpdateBatchButtons();
        var noCode = current?.DeviceId == Position.ReaderId && current.ConsecutiveNoCodeFrames >= 3 && current.Frame is null;
        if (photo.Source is null && current?.DeviceId == Position.ReaderId)
            imageHint.Text = current.ManualRequest ? UiText.Get("Debug.ManualNoPhoto") : noCode ? UiText.Get("Debug.NoBarcodeHint") : current.State is "Preparing" or "Reading" or "Sealing" ? UiText.Get("Debug.ReadingHint") :
                current.Frame is not null ? UiText.Get("Debug.MissingPhotoHint") : "等待本轮扫码照片\n点击“触发扫码”开始读取。";
        // Scanning needs this camera online; a keyed-in request does not.
        var cameraOnline = vm.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == Position.ReaderId)?.IsValid(DateTimeOffset.UtcNow) == true;
        var canCheckCamera = vm.Fresh && vm.Snapshot is { } snapshot && !Position.SupplyDisabled &&
            !ParcelConnectionSetup.IsDeferred(snapshot.Configuration.Effective, Position.ReaderId);
        readStatus.Text = !cameraOnline && current?.State is not ("Reading" or "Preparing" or "Sealing" or "Locked") ?
            canCheckCamera ? UiText.Get("Debug.CheckTrigger") : "" :
            current?.DeviceId != Position.ReaderId ? UiText.Get("Debug.ScanNotStarted") : current?.ManualRequest == true ? UiText.Get("Debug.ManualCommand") : current?.State switch
        { "Reading" or "Preparing" or "Sealing" => UiText.Get("Debug.Reading"), "Locked" => UiText.Get("Debug.LockedBarcode"), "Sending" => UiText.Get("Debug.Sending"), "Faulted" => UiText.Get("Debug.ReadError"), _ => UiText.Get("Debug.ScanNotStarted") };
        photoState.Text = current?.ManualRequest == true ? UiText.Get("Debug.ManualNoImage") : noCode ? UiText.Get("Debug.NoCode") : displayedFrame is null ? UiText.Get("Debug.WaitScan") : UiText.Get("Debug.ResultLocked");
        if (current?.ManualRequest == true) photoCaption.Text = UiText.Get("Debug.ManualRequest");
        fillStatus.Text = requestDirty ? UiText.Get("Debug.DraftChanged") : current?.DeviceId == Position.ReaderId && current.RequestBody is not null ?
            (current.ManualRequest ? "手工输入" : UiText.Get("Debug.CameraRead")) + " · " + Position.Heading : "";
        feedback.Visibility = string.IsNullOrWhiteSpace(feedback.Text) ? Visibility.Collapsed : Visibility.Visible;
        var waiting = batch is not null || observed?.State is "Preparing" or "Reading" or "Sealing" or "Sending";
        trigger.Content = armCommand is not null || beginCommand is not null ? UiText.Get("Debug.TriggerCheck") : current?.State is "Preparing" or "Reading" or "Sealing" ? UiText.Get("Debug.Scanning") : current?.Id is null || current.ManualRequest ? UiText.Get("Debug.Trigger") : UiText.Get("Debug.ScanAgain");
        trigger.IsEnabled = active && !busy && !waiting && vm.IsManagementMode && canCheckCamera;
        stopRead.Visibility = stationDebug && observed?.State is ("Reading" or "Sealing") ? Visibility.Visible : Visibility.Collapsed;
        stopRead.IsEnabled = active && !busy && vm.IsManagementMode && ownedScanSession == observed?.SessionId &&
            ownedScanRound is not null && ownedScanRound == observed?.Id && observed?.State is "Reading" or "Sealing";
        trigger.ToolTip = canCheckCamera ? UiText.Get("Debug.ScanningNoSend") : UiText.Get("Debug.ConfigureCamera");
        ToolTipService.SetShowOnDisabled(trigger, true);
        prepare.Content = prepareCommand is null ? UiText.Get("Debug.PrepareRequest") : UiText.Get("Debug.CheckPrepared");
        prepare.IsEnabled = active && !busy && !waiting && vm.Fresh && vm.IsManagementMode && !string.IsNullOrWhiteSpace(requestBarcode.Text);
        send.Content = sendCommand is null ? UiText.Get("Debug.SendRequest") : UiText.Get("Debug.CheckSent");
        send.IsEnabled = active && !busy && authoritative && vm.Fresh && vm.IsManagementMode && current?.Id == observed?.Id && current?.DeviceId == Position.ReaderId &&
            (sendCommand is not null || !requestDirty && current is { RequestHash: not null, SendAttempts: 0 } && (current.ManualRequest || current.Frame is { } frame && loadedFrame == frame.Id));
        copyResponse.IsEnabled = active && !busy && vm.IsManagementMode && displayedResponse?.Complete == true && originalResponseText is { Length: > 0 };
        var calibration = CameraAcquisitionPresentation.Calibration(vm, Position, stationDebug);
        calibrationStatus.Text = calibration.Label;
        calibrationStatus.SetResourceReference(TextBlock.ForegroundProperty, calibration.Tone);
        calibrationBadge.SetResourceReference(BackgroundProperty, calibration.Tone == "Muted" ? "Soft" : calibration.Tone + "Background");
        calibrationBadge.SetResourceReference(Border.BorderBrushProperty, calibration.Tone == "Muted" ? "Line" : calibration.Tone + "Border");
        calibrationHint.Text = calibration.CanStart && !livePreview.HasLiveFrame ? UiText.Get("Debug.SwitchLive") : calibration.Detail;
        calibrate.IsEnabled = active && !busy && calibration.CanStart && livePreview.HasLiveFrame;
        calibrate.Content = Position.TrayPresence?.Calibrating == true ? "校准中…" : "确认空托盘并校准";
        left.IsEnabled = right.IsEnabled = !busy && batch is null; left.SetResourceReference(BackgroundProperty, position == "left" ? "AccentBackground" : "Surface"); right.SetResourceReference(BackgroundProperty, position == "right" ? "AccentBackground" : "Surface");
    }
    public void SetStationDebug(bool enabled)
    {
        if (stationDebug == enabled) return;
        stationDebug = enabled;
        if (!enabled) { tabs.SelectedIndex = 0; previewTabs.SelectedIndex = 0; }
        requestCard.Visibility = trigger.Visibility = lockedFrameTab.Visibility = barcode.Visibility =
            batchExpander.Visibility = readStatus.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LayoutColumns();
        Buttons();
    }

    void FitRequestCard()
    {
        // The outer scrolling surface measures without a height limit. Give the
        // star rows a finite budget so a large JSON body scrolls inside its box.
        var controls = requestPanel.RowDefinitions.Where(row => row.Height.IsAuto).Sum(row => row.ActualHeight);
        var verticalChrome = requestCard.Padding.Top + requestCard.Padding.Bottom +
            requestCard.BorderThickness.Top + requestCard.BorderThickness.Bottom;
        var minimum = controls + verticalChrome + requestBody.MinHeight + responseBody.MinHeight;
        requestCard.Height = Math.Max(minimum, debugScroll.ViewportHeight - requestCard.Margin.Top - requestCard.Margin.Bottom);
    }

    void LayoutColumns()
    {
        var split = stationDebug && ActualWidth >= 940;
        requestColumn.Width = split ? new(4, GridUnitType.Star) : new(0);
        Grid.SetColumn(requestCard, split ? 1 : 0);
        Grid.SetRow(requestCard, split ? 0 : 1);
        requestCard.Margin = split ? new(14, 6, 4, 8) : new(4, 6, 4, 8);
        FitRequestCard();
    }
    void Scale(double factor) { fit = false; zoom = Math.Clamp(zoom * factor, .02, 16); ApplyZoom(); }
    void ApplyZoom()
    {
        if (displayedFrame is not { } frame) return;
        if (fit && viewport.ActualWidth > 0) zoom = Math.Clamp(Math.Min(Math.Max(1, viewport.ActualWidth - 20) / frame.Width, Math.Max(1, viewport.ActualHeight - 20) / frame.Height), .02, 16);
        imageLayer.LayoutTransform = new ScaleTransform(zoom, zoom); DrawMarks();
    }
    void DrawMarks()
    {
        marks.Children.Clear(); if (overlay.IsChecked != true || displayedFrame is null) return;
        foreach (var symbol in displayedFrame.Symbols.Where(s => s.Text == current?.Barcode && s.Corners.Length == 4))
        {
            var polygon = new Polygon
            {
                Points = new PointCollection(symbol.Corners.Select(p => new Point(p.X, p.Y))),
                StrokeThickness = Math.Max(2 / zoom, 1),
                Fill = Brushes.Transparent
            };
            polygon.SetResourceReference(Shape.StrokeProperty, "VideoMarker");
            marks.Children.Add(polygon);
            // Some 1D engines return collinear corners. Mark the measured line
            // visibly without inventing an axis-aligned bounding rectangle.
            var area = symbol.Corners.Select((p, i) => p.X * symbol.Corners[(i + 1) % 4].Y - p.Y * symbol.Corners[(i + 1) % 4].X).Sum();
            if (Math.Abs(area) < 1) polygon.StrokeThickness = 4 / zoom;
        }
    }
    public void SelectPosition(string value)
    {
        value = value == "right" ? "right" : "left";
        if (value != position && (busy || prepareCommand is not null || armCommand is not null || sendCommand is not null))
        { feedback.Text = UiText.Get("Debug.ReconcileFirst"); return; }
        position = value; generation++; authoritative = false; ClearRoundDisplay(); livePreview.SelectDevice(Position.ReaderId); camera.Text = Position.Heading + " · " + Position.DeviceLabel + "    " + vm.RcsServiceStatus; evidence.SelectPosition(position); Buttons();
    }
    public void Revoke() { Deactivate(); ClearRoundDisplay(); evidence.Revoke(); Buttons(); }
    async Task CloseScanScopeAsync(string session, string? round)
    {
        var closed = await vm.ServiceAsync(() => Inspection.EndCameraInspectionAsync(new(Wire.Id(), session, false, true, "结束单次扫码调试")
        { CloseReadRounds = true, ExpectedRoundId = round }, CancellationToken.None));
        if (ownedScanSession == session) { ownedScanSession = null; ownedScanRound = null; }
        if (active && scope?.SessionId == session) scope = closed;
    }
    async Task CloseOwnedScanAsync(string session, string? round)
    {
        try { await CloseScanScopeAsync(session, round); }
        catch (CameraReadRoundConflictException) { /* Another window owns a newer round; leave it intact. */ }
        catch (Exception error) { vm.Message = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "本轮调试清理未完成，原请求保留；请查看调试记录。"); }
    }
    async Task CloseOwnedManualAsync(string round, Guid runtime)
    {
        try { await vm.ServiceAsync(() => Rounds.CloseUnsentDebugRoundAsync(new(Wire.Id(), runtime, round), CancellationToken.None)); }
        catch (CameraReadRoundConflictException) { /* A newer round belongs to another explicit command. */ }
        catch (Exception error) { vm.Message = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "人工调试退出未完成，原请求保留；请核对原调试状态。"); }
    }
    void Deactivate()
    {
        activation++;
        if (deactivation.IsCompleted) deactivation = ObserveDeactivationAsync();
    }
    async Task ObserveDeactivationAsync()
    {
        try { await CloseAsync(); }
        catch (Exception error) { vm.Message = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "本窗调试结束尚未确认，原身份和证据保留；请核对或再次关闭。"); }
    }
    // Quiescing UI waits is distinct from the Application confirming scope closure.
    public async Task QuiesceAsync()
    {
        active = false; authoritative = false; generation++; timer.Stop(); livePreview.Revoke();
        var owner = lifetime; lifetime = null; owner?.Cancel();
        var operation = batch;
        if (operation is not null) { operation.StopRequested = true; operation.Wait.Cancel(); }
        try
        {
            if (actionFinished is { } action) await action.Task;
            if (operation is not null)
            {
                await operation.Completed.Task;
                if (operation.Finishing is { } finishing) await finishing;
            }
        }
        finally { owner?.Dispose(); }
    }
    public void ConfirmWorkspaceClosed(BusinessReceipt receipt)
    {
        if (receipt.Status != "Restored") throw new InvalidOperationException("管理窗口结束结果尚未确认。");
        ObserveReleasedWorkspace();
    }
    public void ObserveReleasedWorkspace()
    {
        batch?.Wait.Dispose(); batch = null;
        ownedScanSession = ownedScanRound = ownedManualRound = null; ownedManualRuntime = null;
        beginCommand = null; armCommand = null; prepareCommand = null;
        ClearRoundDisplay(); UpdateBatchButtons();
    }
    public async Task CloseAsync()
    {
        await QuiesceAsync();
        if (batch is not null)
        {
            await StopBatchAsync();
            if (batch is not null) throw new InvalidOperationException("原试扫结束未确认，窗口责任保留。");
        }
        if (ownedScanSession is { } session)
        {
            try { await CloseScanScopeAsync(session, ownedScanRound); }
            catch (CameraReadRoundConflictException) { ownedScanSession = ownedScanRound = null; }
        }
        if (ownedManualRound is { } manual && ownedManualRuntime is { } runtime)
        {
            try { await vm.ServiceAsync(() => Rounds.CloseUnsentDebugRoundAsync(new(Wire.Id(), runtime, manual), CancellationToken.None)); }
            catch (CameraReadRoundConflictException) { /* The newer round remains owned by its original request. */ }
            ownedManualRound = null; ownedManualRuntime = null;
        }
        ClearRoundDisplay();
    }
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 4, 0, 6) };
    static void SetText(TextBox box, string value) { if (box.Text != value) box.Text = value; }
    static Button Action(string text) => new() { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
}
