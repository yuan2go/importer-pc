using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly DispatcherTimer feedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    bool feedbackReading;
    partial void InitializePresentationFeedback()
    {
        feedbackTimer.Tick += async (_, _) =>
        {
            if (feedbackReading || closing || closed || !IsVisible || ViewModel.SelectedPage != "workstation" ||
                ViewModel.Application is not IParcelPresentationDiagnostics source) return;
            feedbackReading = true;
            try
            {
                var values = await ViewModel.ServiceAsync(() => source.ReadPresentationFeedbackAsync(CancellationToken.None));
                if (closing || closed || ViewModel.SelectedPage != "workstation") return;
                foreach (var read in values) (read.PositionId == "left" ? ViewModel.Left : ViewModel.Right).ApplyReadFeedback(read);
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (closing || closed) { }
            finally { feedbackReading = false; }
        };
        Loaded += (_, _) => feedbackTimer.Start();
        Closed += (_, _) => feedbackTimer.Stop();
    }
}

public partial class SupplyStationPosition
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll", ExactSpelling = true)] static extern int DwmFlush();
    static readonly SemaphoreSlim compositorFence = new(1, 1);
    static long lastSound;
    string? feedbackShown, slotMeasured;
    bool measuring;
    EventHandler? compositionWaiter;
    internal string? CompositionFailure { get; private set; }
    long presentationGeneration;
    readonly DispatcherTimer cueTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    Brush? originalOutline;
    void InitializeCompositionFeedback()
    {
        cueTimer.Tick += (_, _) => { cueTimer.Stop(); if (originalOutline is not null) PositionOutline.SetCurrentValue(System.Windows.Controls.Border.BorderBrushProperty, originalOutline); };
        Loaded += (_, _) => ObservePresentation();
        Unloaded += (_, _) => { presentationGeneration++; if (compositionWaiter is not null) { CompositionTarget.Rendering -= compositionWaiter; compositionWaiter = null; measuring = false; } cueTimer.Stop(); if (originalOutline is not null) PositionOutline.SetCurrentValue(System.Windows.Controls.Border.BorderBrushProperty, originalOutline); };
    }
    void ObservePresentation()
    {
        if (!IsLoaded || !IsVisible || DataContext is not SupplyStationPositionViewModel p || Host?.ViewModel is not { Fresh: true } vm ||
            vm.Application is not IParcelPresentationDiagnostics source || vm.Snapshot is not { } snapshot ||
            Window.GetWindow(this) is not { WindowState: not WindowState.Minimized }) return;
        string? id = null; string? stage = null;
        if (p.ReadFeedbackIdentity is { } read && feedbackShown != read && p.CameraReadFeedback.Length != 0)
        {
            feedbackShown = id = read; stage = "ReadFeedbackComposed";
            originalOutline ??= PositionOutline.BorderBrush;
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            { PositionOutline.SetCurrentValue(System.Windows.Controls.Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(36, 95, 211))); cueTimer.Stop(); cueTimer.Start(); }
            var now = Stopwatch.GetTimestamp(); var previous = Interlocked.Read(ref lastSound);
            if ((previous == 0 || Stopwatch.GetElapsedTime(previous, now) > TimeSpan.FromSeconds(1)) && Interlocked.CompareExchange(ref lastSound, now, previous) == previous)
                System.Media.SystemSounds.Beep.Play();
        }
        if (id is null && p.CurrentTaskAvailable && p.ShowConfirmedTask && p.Input is { } input && slotMeasured != input.Observation.Id)
        { id = input.Observation.Id; stage = "SlotComposed"; }
        if (id is null || measuring) return;
        measuring = true; var generation = presentationGeneration;
        EventHandler? rendered = null;
        rendered = async (_, _) =>
        {
            CompositionTarget.Rendering -= rendered; compositionWaiter = null;
            try
            {
                // Rendering fires before submission. The idle turn lets WPF submit,
                // then the native fence waits for this process's outstanding presents.
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (!compositorFence.Wait(0)) { CompositionFailure = "DwmFenceBusy"; return; }
                // Diagnostic-only process fence: never hold a business/window drain
                // hostage to a stalled compositor. The sole native slot remains owned
                // until the actual call returns, even if this window has closed.
                var result = await Task.Run(() => { try { return DwmFlush(); } finally { compositorFence.Release(); } });
                if (result != 0) { CompositionFailure = "DwmFlush:0x" + result.ToString("X8"); return; }
                if (generation != presentationGeneration || !IsLoaded || !IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized || vm.Snapshot?.RuntimeId != snapshot.RuntimeId) return;
                if (stage == "SlotComposed" && (p.Input?.Observation.Id != id || !p.ShowConfirmedTask)) return;
                await source.ReportPresentationAsync(snapshot.RuntimeId, id, stage!, Stopwatch.GetTimestamp(), stage == "SlotComposed" ? snapshot.PublishedTicks : 0, CancellationToken.None);
                if (stage == "SlotComposed") slotMeasured = id;
            }
            catch (Exception error) { CompositionFailure = error.GetType().Name + ": " + error.Message; }
            finally { measuring = false; }
        };
        compositionWaiter = rendered; CompositionTarget.Rendering += rendered;
        InvalidateVisual();
    }
}
