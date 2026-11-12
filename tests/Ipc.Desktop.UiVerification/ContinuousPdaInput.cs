using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyContinuousPdaInput(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS PDA-NATIVE " + message); }
        lines.Add("Windows SendInput -> HWND -> WPF text/key events; real Application/SQLite/built-in RCS with synthetic loopback peer. Physical PDA/customer RCS/site NOT_RUN.");
        var clock = new PdaRoundClock();
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "continuous-pda"), deferred: true) { StationClock = clock };
        await h.StartAsync();
        var application = DispatchProxy.Create<IBarcodeTransportFixture, BarcodeTransportProbe>();
        var transport = (BarcodeTransportProbe)(object)application; transport.Inner = h.App;
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "菜鸟供包台 · PDA合成验证" };
        window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        {
            for (var i = 0; i < 150; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("Continuous PDA input did not converge; commands=" + string.Join(",", transport.Commands.Select(c => c.Barcode)));
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanAdmit);
            var position = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var entry = (TextBox)position.FindName("BarcodeEntry"); var feedback = (TextBlock)position.FindName("InputFeedback");
            var other = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
            var otherEntry = (TextBox)other.FindName("BarcodeEntry");
            Check(position.FindName("BarcodeSource") is null && other.FindName("BarcodeSource") is null, "neither lane asks the operator to choose a source");
            var textEvents = 0; var enterEvents = 0;
            entry.PreviewTextInput += (_, _) => textEvents++;
            entry.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key == System.Windows.Input.Key.Enter) enterEvents++; }), true);
            window.Activate(); entry.Focus(); await Flush();
            Task Text(string value, bool enter = false) => BarcodeNativeKeyboard.SendAsync(window, entry, value.Select(c => (ushort)c).Concat(enter ? new ushort[] { 0x0D } : []).ToArray());
            Task Key(ushort key) => BarcodeNativeKeyboard.SendAsync(window, entry, [key]);
            async Task Accepted(string code) => await Until(() => !entry.IsReadOnly && !vm.Busy && vm.Left.Barcode == code && vm.Left.SlotAvailable);
            async Task NextRound(string side = "left")
            {
                clock.Advance();
                await h.NextVehicleAsync(side);
                await Until(() => vm.Snapshot!.Positions!.Single(p => p.Id == side).CurrentRoundId is null);
            }

            const string a = "000123450001", b = "000123450002";
            await Text("123456");
            await NextRound(); await Key(0x0D); await Flush();
            Check(transport.Commands.Count == 0 && h.Peer.Requests.IsEmpty && entry.Text == "123456" && ((Button)position.FindName("RebaseInput")).IsVisible,
                "unfinished native PDA burst and its old Enter cannot submit to a replacement car");
            CaptureInternal(evidence, "arrival-old-pda-draft", (Grid)window.FindName("ClientArea"), lines);
            entry.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "清空输入")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Text(a, enter: true); await Accepted(a);
            var first = vm.Left.Operation!; var frozen = Wire.Encode(first.Wcs!.Request);
            var prepared = entry.IsKeyboardFocused && entry.Text == a && entry.SelectionLength == a.Length;
            // No Text assignment, clear, refocus or selection adjustment between A and B.
            await NextRound(); await Text(b, enter: true); await Accepted(b);
            Check(transport.Commands.Select(c => c.Barcode).SequenceEqual(new[] { a, b }) && h.Peer.Requests.Count == 2 &&
                vm.Left.Operation!.Id != first.Id && transport.Commands.All(c => c.Kind == ParcelBarcodeInputKind.PdaKeyboard) &&
                h.Peer.Requests.All(body => RequestSource(body) == "PDA"),
                "consecutive native bursts automatically send PDA for exactly A and B as distinct operations; kinds=" + string.Join(",", transport.Commands.Select(c => c.Kind)));
            Check(prepared, "A remains displayed and selected after PDA Enter without moving focus");
            var firstAfter = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == first.Id);
            Check(Wire.Encode(firstAfter.Wcs!.Request) == frozen && firstAfter.Wcs.Attempts == 1 && firstAfter.Wcs.State == StageState.Accepted,
                "B leaves A's frozen request and accepted responsibility intact");
            Check(textEvents == 6 + a.Length + b.Length && enterEvents == 3,
                "both scans traverse real WPF PreviewTextInput and Enter events character by character");
            await Key(0x0D); await Flush();
            Check(transport.Commands.Count == 2 && h.Peer.Requests.Count == 2, "extra Enter on a retained result creates no new command");
            entry.Select(entry.Text.Length, 0); // Simulate a click moving the caret on the retained result.
            await Text("000123"); await Flush();
            Check(entry.Text == "000123" && vm.Left.Barcode == b && vm.Left.SlotAvailable && transport.Commands.Count == 2,
                "first manual character replaces the retained result while its task remains displayed; typing alone never submits");
            await Key(0x25); await Text("9"); await Flush(); // Left arrow + insertion inside the active draft.
            var start = entry.SelectionStart; otherEntry.Focus(); await Flush(); entry.Focus(); await Flush();
            for (var i = 0; i < 3; i++) { await vm.RefreshAsync(); await Flush(); }
            Check(entry.Text == "0001293" && entry.SelectionStart == start && entry.SelectionLength == 0 && entry.IsKeyboardFocused,
                "manual corrections, focus return and snapshot refresh preserve the draft and caret");
            await Key(0x08); await Flush();
            Check(entry.Text == "000123", "native Backspace edits the active draft normally");
            // Exercise insertion/deletion at every caret offset after real key-up acknowledgement.
            // Neither Text nor selection is assigned to make these outcomes pass.
            for (var round = 0; round < 16; round++)
            {
                var offset = round % 7;
                await Key(0x24); // Home
                for (var move = 0; move < offset; move++) await Key(0x27); // Right
                await Text("9");
                if (round % 2 == 0)
                { otherEntry.Focus(); await Flush(); entry.Focus(); await vm.RefreshAsync(); await Flush(); }
                await Key(0x08);
                Check(entry.Text == "000123" && entry.SelectionStart == offset && entry.SelectionLength == 0 && transport.Commands.Count == 2 && h.Peer.Requests.Count == 2,
                    "acknowledged native insertion/Backspace preserves draft/caret without submission; round=" + round + "; offset=" + offset);
            }
            await NextRound();
            Check(entry.Text == "000123" && ((Button)position.FindName("RebaseInput")).IsVisible,
                "a vehicle change preserves an unfinished draft and requires explicit confirmation for the new car");
            ((Button)position.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
            Check(transport.Commands.Count == 2 && h.Peer.Requests.Count == 2,
                "queued Enter or submit on the old arrival cannot silently redirect the draft");
            ((Button)position.FindName("RebaseInput")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)position.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Accepted("000123");
            Check(transport.Commands.Last().Kind == ParcelBarcodeInputKind.Keyboard && h.Peer.Requests.Count == 3,
                "manual bottom submit uses the same guarded path and preserves the unified character-input channel");

            // Simulate lost/indeterminate UI replies after the real Application committed.
            // Rechecking must forward the identical command, never authorize a second request.
            foreach (var reply in new[] { "Pending", "Lost" })
            {
                await NextRound(); entry.Focus(); await Flush(); transport.NextReply = reply;
                var expectedRequests = h.Peer.Requests.Count + 1;
                var code = reply == "Pending" ? "000123450004" : "000123450005";
                await Text(code, enter: true);
                await Until(() => !vm.Busy && entry.IsReadOnly && feedback.Text.Contains("提交结果未确认") && h.Peer.Requests.Count == expectedRequests);
                var command = transport.Commands.Last(); var sends = h.Peer.Requests.Count; var calls = transport.Commands.Count;
                await Text("999"); await Flush();
                Check(entry.Text == code && entry.IsReadOnly && transport.Commands.Count == calls && h.Peer.Requests.Count == sends,
                    reply + " reply locks the original input and rejects subsequent scanner text");
                await Key(0x0D); await Accepted(code);
                Check(transport.Commands.Count == calls + 1 && transport.Commands.Last() == command && h.Peer.Requests.Count == sends,
                    reply + " recheck retains the exact command and recovers its receipt without another RCS request");
            }

            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Peer.Hold = hold;
            try
            {
                await NextRound(); await Text("000123450008", enter: true);
                await Until(() => h.Peer.Requests.Count == 6 && !vm.Busy && !entry.IsReadOnly && vm.Right.CanSubmitBarcode);
                Check(h.Peer.Requests.Count == 6 && !vm.Busy && !entry.IsReadOnly && vm.Right.CanSubmitBarcode, "durable acceptance releases global UI Busy while the original RCS call remains in flight");
                var calls = transport.Commands.Count;
                await Text("999", enter: true); await Flush();
                Check(entry.Text == "999" && transport.Commands.Count == calls && h.Peer.Requests.Count == 6,
                    "scanner text and Enter during submission cannot create a second command");
            }
            finally { hold.TrySetResult(); h.Peer.Hold = null; }
            await Accepted("000123450008");

            await NextRound(); h.Peer.Mode = "malformed"; entry.Focus(); await Flush(); await Text("000123450006", enter: true);
            await Until(() => !entry.IsReadOnly && !vm.Busy && vm.Left.Operation?.Wcs?.State == StageState.Unknown);
            var unknown = vm.Left.Operation!; var unknownSends = h.Peer.Requests.Count;
            await Text("000123450007", enter: true);
            await Until(() => !entry.IsReadOnly && !vm.Busy && feedback.Text.StartsWith("本位暂不能接收新条码"));
            Check(entry.Text == "000123450007" && vm.Left.Operation!.Id == unknown.Id &&
                vm.Left.Operation.Wcs is { State: StageState.Unknown, Attempts: 1 } && h.Peer.Requests.Count == unknownSends,
                "a new barcode replaces the displayed input; the UI availability guard refuses submission while Unknown responsibility preserves the draft and prevents HTTP replay");
            var draft = entry.Text; entry.Select(3, 2); await vm.RefreshAsync(); await Flush();
            Check(entry.Text == draft && entry.SelectionStart == 3 && entry.SelectionLength == 2,
                "rejected manual input is not selected over or erased by refresh");
            var sentCodes = h.Peer.Requests.Select(body => { using var json = JsonDocument.Parse(body); return json.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString(); }).ToArray();
            Check(sentCodes.SequenceEqual(new[] { a, b, "000123", "000123450004", "000123450005", "000123450008", "000123450006" }),
                "actual RCS wire loadCode values contain only complete intended barcodes, with no concatenation or repeated send");

            h.Peer.Mode = "assigned"; otherEntry.Focus(); await Flush();
            Task RightKeys(params ushort[] keys) => BarcodeNativeKeyboard.SendAsync(window, otherEntry, keys);
            async Task RightAccepted(string code) => await Until(() => !otherEntry.IsReadOnly && !vm.Busy && vm.Right.Barcode == code && vm.Right.SlotAvailable);
            const string typed = "000123450010", pasted = "000123450011", scanned = "000123450012";
            foreach (var character in typed)
            {
                await RightKeys(character);
                await Task.Delay(65);
            }
            await RightKeys(0x0D); await RightAccepted(typed);
            Check(transport.Commands.Last() is { PositionId: "right", Kind: ParcelBarcodeInputKind.Keyboard } && RequestSource(h.Peer.Requests.Last()) == "MANUAL",
                "ordinary native typing and Enter on the right are classified as MANUAL while the left retains its own result");
            var previousClipboard = Clipboard.GetDataObject();
            try
            {
                await NextRound("right"); Clipboard.SetText(pasted); otherEntry.Paste(); await RightKeys(0x0D); await RightAccepted(pasted);
                Check(transport.Commands.Last().Kind == ParcelBarcodeInputKind.Keyboard && RequestSource(h.Peer.Requests.Last()) == "MANUAL",
                    "a whole pasted barcode stays MANUAL even when Enter follows immediately");
            }
            finally
            {
                if (previousClipboard is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previousClipboard, true);
            }
            await NextRound("right"); await RightKeys([.. scanned.Select(character => (ushort)character), 0x0D]); await RightAccepted(scanned);
            Check(transport.Commands.Last() is { PositionId: "right", Kind: ParcelBarcodeInputKind.PdaKeyboard } &&
                RequestSource(h.Peer.Requests.Last()) == "PDA" && vm.Left.Operation?.Id == unknown.Id && h.Peer.Requests.Count == unknownSends + 3,
                "the next right-hand burst is identified as PDA without using a previous draft or altering the left operation; kind=" + transport.Commands.Last().Kind + "; source=" + RequestSource(h.Peer.Requests.Last()) + "; left=" + vm.Left.Operation?.Id + "/" + unknown.Id + "; sends=" + h.Peer.Requests.Count + "/" + (unknownSends + 3));
            await ManagementModeFixture.Authorize(h.App);
            var original = (await h.App.ReadCameraOperationDetailAsync(first.Id, default))!.Original;
            Check(original.Wcs!.Request.IntegrationRequest!.Body == h.Peer.Requests.First() && original.Wcs.Attempts == 1 && original.Wcs.State == StageState.Accepted,
                "A's persisted full frozen RCS bytes still match the original wire request after all later input");
            var otherWindow = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "菜鸟供包台 · 同实例第二窗口" };
            try
            {
                var round = vm.Right.CurrentRound!.Id; var sends = h.Peer.Requests.Count;
                otherWindow.Show();
                // Showing the window starts its own asynchronous refresh. A concurrent push can
                // supersede RefreshAsync, so one dispatcher flush is not a
                // completed projection. Wait for the actual shared-round result.
                for (var i = 0; i < 150; i++)
                {
                    await otherWindow.ViewModel.RefreshAsync(); await Flush();
                    if (otherWindow.ViewModel.Fresh && otherWindow.ViewModel.Right.CurrentRound?.Id == round &&
                        otherWindow.ViewModel.Right.Operation?.Id == vm.Right.Operation?.Id) break;
                    await Task.Delay(25);
                }
                Check(otherWindow.ViewModel.Right.CurrentRound?.Id == round && otherWindow.ViewModel.Right.Operation?.Id == vm.Right.Operation?.Id,
                    "two native WPF windows project the same Application-owned current round");
                var duplicate = await h.Command("right", scanned, ParcelBarcodeInputKind.PdaKeyboard);
                var receipts = await Task.WhenAll(h.App.SubmitParcelBarcodeAsync(duplicate, default), h.App.SubmitParcelBarcodeAsync(duplicate, default));
                Check(receipts.All(r => r.Status == "Rejected") && h.Peer.Requests.Count == sends && vm.Right.CurrentRound.Id == round,
                    "multiple subscribers and competing same-round commands cannot acquire another first call");
            }
            finally { otherWindow.Close(); await Flush(); window.Activate(); }
        }
        finally { window.Close(); }

        static string? RequestSource(string body)
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("source").GetString();
        }
    }
}

public interface IBarcodeTransportFixture : IHistoryProjectionFixture, IParcelWorkstation { }
public class BarcodeTransportProbe : ProjectionProbe
{
    public readonly List<SubmitParcelBarcode> Commands = [];
    public string? NextReply;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name != nameof(IParcelWorkstation.SubmitParcelBarcodeAsync)) return base.Invoke(method, arguments);
        return Submit((SubmitParcelBarcode)arguments![0]!, (CancellationToken)arguments[1]!);
    }
    async Task<ParcelBarcodeReceipt> Submit(SubmitParcelBarcode command, CancellationToken token)
    {
        Commands.Add(command); var reply = NextReply; NextReply = null;
        // Explicit UI-only terminal receipt projection: no business request is
        // fabricated or passed to the real Application for these status cases.
        if (reply?.StartsWith("Terminal/", StringComparison.Ordinal) == true)
            return new(reply[9..], "Synthetic UI-only terminal receipt", null, null, 0);
        var receipt = await ((IParcelWorkstation)Inner).SubmitParcelBarcodeAsync(command, token);
        if (reply == "Lost") throw new IOException("Synthetic lost UI reply after real durable admission");
        return reply == "Pending" ? receipt with { Status = "Pending", Detail = "Synthetic indeterminate UI reply" } : receipt;
    }
}

// Real OS key down/up packets, not RaiseEvent or replacing TextBox.Text.
// Fail if the interactive desktop, foreground HWND or intended focus is missing.
static class BarcodeNativeKeyboard
{
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetCursorPos(int x, int y);
    static async Task WaitForForegroundAsync(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var budget = System.Diagnostics.Stopwatch.StartNew();
        // A background process's Activate() can be refused by Windows. Wait for
        // actual activation; never inject input into another foreground app.
        while (window.IsVisible && (!window.IsActive || GetForegroundWindow() != handle) && budget.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(50);
    }
    public static async Task ClickAsync(Window window, FrameworkElement target, Point offset)
    {
        await WaitForForegroundAsync(window);
        if (!window.IsActive || GetForegroundWindow() != new WindowInteropHelper(window).Handle || !target.IsVisible)
            throw new InvalidOperationException($"Native mouse input requires the foreground test HWND and a visible target; active={window.IsActive}; foreground={GetForegroundWindow()}; expected={new WindowInteropHelper(window).Handle}; visible={target.IsVisible}");
        var point = target.PointToScreen(offset);
        if (!SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y))) throw new Win32Exception(Marshal.GetLastWin32Error());
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MouseButtonEventHandler acknowledged = (_, e) => { if (e.ChangedButton == MouseButton.Left) processed.TrySetResult(); };
        window.AddHandler(Mouse.MouseUpEvent, acknowledged, true);
        try
        {
            Input[] inputs = [new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 2 } } },
                new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 4 } } }];
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
            await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        finally { window.RemoveHandler(Mouse.MouseUpEvent, acknowledged); }
    }
    public static async Task SendAsync(Window window, TextBox entry, ushort[] keys, FrameworkElement? initialFocus = null)
    {
        window.Dispatcher.VerifyAccess();
        if (keys.Length == 0) throw new ArgumentException("At least one native key is required", nameof(keys));
        var neededActivation = !window.IsActive || GetForegroundWindow() != new WindowInteropHelper(window).Handle;
        await WaitForForegroundAsync(window);
        if (neededActivation)
        {
            (initialFocus ?? entry).Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        var foreground = GetForegroundWindow(); var expectedWindow = new WindowInteropHelper(window).Handle;
        var modifiers = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Where(k => (GetAsyncKeyState(k) & 0x8000) != 0).ToArray();
        if (!window.IsActive || foreground != expectedWindow || !(initialFocus ?? entry).IsKeyboardFocused || modifiers.Length != 0)
            throw new InvalidOperationException($"Native barcode input requires the foreground test HWND, barcode focus and no held modifiers; active={window.IsActive}; foreground={foreground}; expected={expectedWindow}; focus={entry.IsKeyboardFocused}; held={string.Join(",", modifiers)}");
        var inputs = keys.SelectMany(key => new[] {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } }
        }).ToArray();
        // SendInput only queues OS input. Dispatcher idle can run before Windows
        // delivers it. Subscribe before injection and wait for each actual WPF
        // key-up, including handled/read-only paths, before assertions or focus moves.
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = 0;
        KeyEventHandler acknowledged = (_, e) =>
        {
            if (received >= keys.Length) return;
            var expected = KeyInterop.KeyFromVirtualKey(keys[received]);
            if (e.Key != expected)
            { processed.TrySetException(new InvalidOperationException($"Unexpected native key-up {e.Key}; expected {expected} at {received}")); return; }
            if (++received == keys.Length) processed.TrySetResult();
        };
        var acknowledgementOwner = initialFocus is null ? (UIElement)entry : window;
        acknowledgementOwner.AddHandler(Keyboard.KeyUpEvent, acknowledged, true);
        try
        {
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Native barcode SendInput was incomplete");
            try { await processed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException error)
            { throw new TimeoutException($"Native input acknowledgement timed out: {received}/{keys.Length}; text={entry.Text}; caret={entry.SelectionStart}/{entry.SelectionLength}; focus={entry.IsKeyboardFocused}", error); }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        finally { acknowledgementOwner.RemoveHandler(Keyboard.KeyUpEvent, acknowledged); }
    }
}

sealed class PdaRoundClock : TimeProvider
{
    long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref ticks);
    public void Advance() => Interlocked.Add(ref ticks, TimeSpan.FromMilliseconds(3000).Ticks);
}
