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
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "continuous-pda"), deferred: true);
        await h.StartAsync();
        var application = DispatchProxy.Create<IBarcodeTransportFixture, BarcodeTransportProbe>();
        var transport = (BarcodeTransportProbe)(object)application; transport.Inner = h.App;
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
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
            var textEvents = 0; var enterEvents = 0;
            entry.PreviewTextInput += (_, _) => textEvents++;
            entry.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key == Key.Enter) enterEvents++; }), true);
            ((MenuItem)position.FindName("PdaSource")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.Activate(); entry.Focus(); await Flush();
            void Text(string value, bool enter = false) => BarcodeNativeKeyboard.Send(window, entry, value.Select(c => (ushort)c).Concat(enter ? new ushort[] { 0x0D } : []).ToArray());
            void Key(ushort key) => BarcodeNativeKeyboard.Send(window, entry, [key]);
            async Task Accepted(string code) => await Until(() => !entry.IsReadOnly && !vm.Busy && vm.Left.Barcode == code && vm.Left.SlotAvailable);

            const string a = "000123450001", b = "000123450002";
            Text(a, enter: true); await Accepted(a);
            var first = vm.Left.Operation!; var frozen = Wire.Encode(first.Wcs!.Request);
            var prepared = entry.IsKeyboardFocused && entry.Text == a && entry.SelectionLength == a.Length;
            // No Text assignment, clear, refocus or selection adjustment between A and B.
            Text(b, enter: true); await Accepted(b);
            Check(transport.Commands.Select(c => c.Barcode).SequenceEqual(new[] { a, b }) && h.Peer.Requests.Count == 2 &&
                vm.Left.Operation!.Id != first.Id && transport.Commands.All(c => c.Kind == ParcelBarcodeInputKind.PdaKeyboard),
                "consecutive PDA bursts submit exactly A then B as distinct commands/operations, never A+B");
            Check(prepared, "A remains displayed and selected after PDA Enter without moving focus");
            var firstAfter = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == first.Id);
            Check(Wire.Encode(firstAfter.Wcs!.Request) == frozen && firstAfter.Wcs.Attempts == 1 && firstAfter.Wcs.State == StageState.Accepted,
                "B leaves A's frozen request and accepted responsibility intact");
            Check(textEvents == a.Length + b.Length && enterEvents == 2,
                "both scans traverse real WPF PreviewTextInput and Enter events character by character");
            Key(0x0D); await Flush();
            Check(transport.Commands.Count == 2 && h.Peer.Requests.Count == 2, "extra Enter on a retained result creates no new command");

            ((MenuItem)position.FindName("KeyboardSource")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            entry.Select(entry.Text.Length, 0); // Simulate a click moving the caret on the retained result.
            Text("000123"); await Flush();
            Check(entry.Text == "000123" && vm.Left.Barcode == b && vm.Left.SlotAvailable && transport.Commands.Count == 2,
                "first manual character replaces the retained result while its task remains displayed; typing alone never submits");
            Key(0x25); Text("9"); await Flush(); // Left arrow + insertion inside the active draft.
            var start = entry.SelectionStart; otherEntry.Focus(); await Flush(); entry.Focus(); await Flush();
            for (var i = 0; i < 3; i++) { await vm.RefreshAsync(); await Flush(); }
            Check(entry.Text == "0001293" && entry.SelectionStart == start && entry.SelectionLength == 0 && entry.IsKeyboardFocused,
                "manual corrections, focus return and snapshot refresh preserve the draft and caret");
            Key(0x08); await Flush();
            Check(entry.Text == "000123", "native Backspace edits the active draft normally");
            ((Button)position.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Accepted("000123");
            Check(transport.Commands.Last().Kind == ParcelBarcodeInputKind.Keyboard && h.Peer.Requests.Count == 3,
                "manual bottom submit uses the same guarded path and preserves the declared source");

            // Simulate lost/indeterminate UI replies after the real Application committed.
            // Rechecking must forward the identical command, never authorize a second request.
            foreach (var reply in new[] { "Pending", "Lost" })
            {
                entry.Focus(); await Flush(); transport.NextReply = reply;
                var code = reply == "Pending" ? "000123450004" : "000123450005";
                Text(code, enter: true);
                await Until(() => !vm.Busy && entry.IsReadOnly && feedback.Text.Contains(reply == "Pending" ? "结果待核对" : "提交结果未确认"));
                var command = transport.Commands.Last(); var sends = h.Peer.Requests.Count; var calls = transport.Commands.Count;
                Text("999"); await Flush();
                Check(entry.Text == code && entry.IsReadOnly && transport.Commands.Count == calls && h.Peer.Requests.Count == sends,
                    reply + " reply locks the original input and rejects subsequent scanner text");
                Key(0x0D); await Accepted(code);
                Check(transport.Commands.Count == calls + 1 && transport.Commands.Last() == command && h.Peer.Requests.Count == sends,
                    reply + " recheck retains the exact command and recovers its receipt without another RCS request");
            }

            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Peer.Hold = hold;
            try
            {
                Text("000123450008", enter: true);
                for (var i = 0; i < 150 && h.Peer.Requests.Count != 6; i++) { await Flush(); await Task.Delay(25); }
                Check(h.Peer.Requests.Count == 6 && vm.Busy && entry.IsReadOnly, "in-flight RCS request retains its original locked input");
                var calls = transport.Commands.Count;
                Text("999", enter: true); await Flush();
                Check(entry.Text == "000123450008" && transport.Commands.Count == calls && h.Peer.Requests.Count == 6,
                    "scanner text and Enter during submission cannot create a second command");
            }
            finally { hold.TrySetResult(); h.Peer.Hold = null; }
            await Accepted("000123450008");

            h.Peer.Mode = "malformed"; entry.Focus(); await Flush(); Text("000123450006", enter: true);
            await Until(() => !entry.IsReadOnly && !vm.Busy && vm.Left.Operation?.Wcs?.State == StageState.Unknown);
            var unknown = vm.Left.Operation!; var unknownSends = h.Peer.Requests.Count;
            Text("000123450007", enter: true);
            await Until(() => !entry.IsReadOnly && !vm.Busy && feedback.Text.StartsWith("未准入"));
            Check(entry.Text == "000123450007" && vm.Left.Operation!.Id == unknown.Id &&
                vm.Left.Operation.Wcs is { State: StageState.Unknown, Attempts: 1 } && h.Peer.Requests.Count == unknownSends,
                "a new barcode replaces the displayed Unknown result but original responsibility rejects admission, preserves the draft and prevents HTTP replay");
            var draft = entry.Text; entry.Select(3, 2); await vm.RefreshAsync(); await Flush();
            Check(entry.Text == draft && entry.SelectionStart == 3 && entry.SelectionLength == 2,
                "rejected manual input is not selected over or erased by refresh");
            var sentCodes = h.Peer.Requests.Select(body => { using var json = JsonDocument.Parse(body); return json.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString(); }).ToArray();
            Check(sentCodes.SequenceEqual(new[] { a, b, "000123", "000123450004", "000123450005", "000123450008", "000123450006" }),
                "actual RCS wire loadCode values contain only complete intended barcodes, with no concatenation or repeated send");
            await ManagementModeFixture.Authorize(h.App);
            var original = (await h.App.ReadCameraOperationDetailAsync(first.Id, default))!.Original;
            Check(original.Wcs!.Request.IntegrationRequest!.Body == h.Peer.Requests.First() && original.Wcs.Attempts == 1 && original.Wcs.State == StageState.Accepted,
                "A's persisted full frozen RCS bytes still match the original wire request after all later input");
        }
        finally { window.Close(); }
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
    [StructLayout(LayoutKind.Explicit)] struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    public static void Send(Window window, TextBox entry, ushort[] keys)
    {
        if (!window.IsActive || GetForegroundWindow() != new WindowInteropHelper(window).Handle || !entry.IsKeyboardFocused ||
            new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
            throw new InvalidOperationException("Native barcode input requires the foreground test HWND, barcode focus and no held modifiers");
        var inputs = keys.SelectMany(key => new[] {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } }
        }).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Native barcode SendInput was incomplete");
    }
}
