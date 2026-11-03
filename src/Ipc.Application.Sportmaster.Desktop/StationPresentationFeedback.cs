using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly object presentationUiGate = new();
    IParcelPresentationSubscription? presentationSubscription;
    readonly CancellationTokenSource presentationLifetime = new();
    ParcelPresentationUpdate? pendingPresentation;
    ParcelPresentationUpdate? latestPresentation;
    CancellationTokenSource? presentationSnapshotCancellation;
    bool presentationDispatchQueued;
    volatile bool presentationEnded;
    long appliedPresentationTicks;
    Task presentationSnapshotApply = Task.CompletedTask;
    Task presentationPump = Task.CompletedTask;
    internal string? PresentationStreamFailure { get; private set; }
    ParcelPresentationUpdate? snapshotWaiting;
    partial void InitializePresentationFeedback()
    {
        Loaded += (_, _) =>
        {
            if (presentationSubscription is not null || ViewModel.Application is not IParcelPresentationSource source) return;
            try
            {
                var subscription = source.SubscribePresentation(); presentationSubscription = subscription;
                var token = presentationLifetime.Token;
                presentationPump = Task.Run(() => PumpPresentationAsync(subscription, token));
            }
            catch (Exception error) { ViewModel.MarkReadFailure("实时状态订阅失败：" + error.Message); }
        };
        Closed += async (_, _) =>
        {
            presentationLifetime.Cancel(); presentationSubscription?.Dispose();
            try { await presentationPump; }
            catch (Exception error) { PresentationStreamFailure = error.GetType().Name + ": " + error.Message; }
            finally { presentationLifetime.Dispose(); }
        };
    }
    async Task PumpPresentationAsync(IParcelPresentationSubscription subscription, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var value = await subscription.ReadAsync(token).ConfigureAwait(false);
                lock (presentationUiGate)
                {
                    latestPresentation = CombinePresentation(latestPresentation, value);
                    pendingPresentation = CombinePresentation(pendingPresentation, latestPresentation);
                    if (latestPresentation.Failure is not null) presentationSnapshotCancellation?.Cancel();
                    QueuePresentationDispatch();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            presentationEnded = true;
            if (!token.IsCancellationRequested && !Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() => ViewModel.MarkReadFailure("实时状态来源已结束；旧结果不可用于投递。"));
        }
        catch (Exception error)
        {
            presentationEnded = true;
            PresentationStreamFailure = error.GetType().Name + ": " + error.Message;
            if (!token.IsCancellationRequested && !Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() => ViewModel.MarkReadFailure("实时状态订阅中断：" + error.GetType().Name));
        }
    }
    static ParcelPresentationUpdate CombinePresentation(ParcelPresentationUpdate? prior, ParcelPresentationUpdate next)
    {
        if (prior is null || prior.RuntimeId != next.RuntimeId) return next;
        var envelope = next.Version >= prior.Version ? next : prior;
        var snapshot = prior.Snapshot is null || next.Snapshot is { } candidate && candidate.PublishedTicks >= prior.Snapshot.PublishedTicks
            ? next.Snapshot : prior.Snapshot;
        return envelope with { Snapshot = snapshot };
    }
    // One pending render callback, regardless of producer rate. The subscription and
    // this slot both keep latest state; neither is a queue of business observations.
    void QueuePresentationDispatch()
    {
        if (presentationDispatchQueued || presentationLifetime.IsCancellationRequested) return;
        presentationDispatchQueued = true;
        Dispatcher.BeginInvoke(new Action(ApplyPresentationQueue), DispatcherPriority.Render);
    }
    void ApplyPresentationQueue()
    {
        try
        {
            ParcelPresentationUpdate? value;
            lock (presentationUiGate)
            {
                value = pendingPresentation is null ? null : CombinePresentation(pendingPresentation, latestPresentation!);
                pendingPresentation = null;
            }
            if (closing || closed || value is null) return;
            void FastFeedback()
            {
                // A classified continuation belongs to the accompanying business
                // snapshot. Applying its newer watermark first would briefly hide
                // the same parcel's confirmed chute while that snapshot is awaited.
                bool AwaitingContinuation(string position, string? observation) =>
                    value.Snapshot?.Inputs?.SingleOrDefault(i => i.PositionId == position) is { } input &&
                    input.Observation.Id == observation && ParcelInputReasons.IsSamePlacement(input) &&
                    (position == "left" ? ViewModel.Left : ViewModel.Right).Input?.Observation.Id != observation;
                foreach (var frame in value.CameraReads)
                    if (frame.Frame.Outcome != Ipc.Platform.ObservationReadOutcome.Codes ||
                        !value.Reads.Any(r => r.PositionId == frame.PositionId && r.DetectedTicks == frame.Frame.CompletedTicks && AwaitingContinuation(r.PositionId, r.ObservationId)))
                        (frame.PositionId == "left" ? ViewModel.Left : ViewModel.Right).ApplyCameraReadFeedback(value.RuntimeId, frame);
                foreach (var state in value.Readers)
                {
                    ViewModel.Left.ApplyReaderFeedback(value.RuntimeId, state);
                    ViewModel.Right.ApplyReaderFeedback(value.RuntimeId, state);
                }
                foreach (var read in value.Reads)
                    if (!AwaitingContinuation(read.PositionId, read.ObservationId))
                        (read.PositionId == "left" ? ViewModel.Left : ViewModel.Right).ApplyReadFeedback(read);
            }
            FastFeedback();
            if (value.Failure is { } failure)
            { ViewModel.MarkReadFailure(failure); return; }
            if (value.Snapshot is { } snapshot && snapshot.PublishedTicks > appliedPresentationTicks)
            {
                if (presentationSnapshotApply.IsCompleted)
                {
                    CancellationTokenSource cancellation;
                    lock (presentationUiGate)
                    {
                        cancellation = CancellationTokenSource.CreateLinkedTokenSource(presentationLifetime.Token);
                        presentationSnapshotCancellation = cancellation;
                        if (latestPresentation?.Failure is not null) cancellation.Cancel();
                    }
                    presentationSnapshotApply = ApplyPublishedSnapshotAsync(value, cancellation);
                    pendingRefresh = Task.WhenAll(pendingRefresh, presentationSnapshotApply);
                }
                else snapshotWaiting = value;
            }
        }
        catch (Exception error) { if (!closing && !closed) ViewModel.MarkReadFailure("实时状态应用失败：" + error.Message); }
        finally
        {
            lock (presentationUiGate)
            {
                presentationDispatchQueued = false;
                if (pendingPresentation is not null && !closing && !closed) QueuePresentationDispatch();
            }
        }
    }
    async Task ApplyPublishedSnapshotAsync(ParcelPresentationUpdate value, CancellationTokenSource cancellation)
    {
        try
        {
            await ViewModel.AcceptPublishedSnapshotAsync(value.Snapshot!, cancellation.Token);
            if (closing || closed) return;
            if (presentationEnded) { ViewModel.MarkReadFailure("实时状态订阅已结束；旧结果不可用于投递。"); return; }
            string? failure; lock (presentationUiGate) failure = latestPresentation?.Failure;
            if (failure is not null) { ViewModel.MarkReadFailure(failure); return; }
            appliedPresentationTicks = value.Snapshot!.PublishedTicks;
            EnforceManagementModeView();
            foreach (var frame in value.CameraReads)
                (frame.PositionId == "left" ? ViewModel.Left : ViewModel.Right).ApplyCameraReadFeedback(value.RuntimeId, frame);
            foreach (var state in value.Readers) { ViewModel.Left.ApplyReaderFeedback(value.RuntimeId, state); ViewModel.Right.ApplyReaderFeedback(value.RuntimeId, state); }
            foreach (var read in value.Reads) (read.PositionId == "left" ? ViewModel.Left : ViewModel.Right).ApplyReadFeedback(read);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!closing && !closed) ViewModel.MarkReadFailure("实时状态更新失败：" + error.Message); }
        finally
        {
            lock (presentationUiGate)
            {
                if (ReferenceEquals(presentationSnapshotCancellation, cancellation)) presentationSnapshotCancellation = null;
                cancellation.Dispose();
                if (snapshotWaiting is { } waiting && !closing && !closed)
                {
                    pendingPresentation = CombinePresentation(pendingPresentation, CombinePresentation(waiting, latestPresentation!));
                    snapshotWaiting = null; QueuePresentationDispatch();
                }
            }
        }
    }

}

public partial class SupplyStationPosition
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll", ExactSpelling = true)] static extern int DwmFlush();
    static readonly SemaphoreSlim compositorFence = new(1, 1);
    static readonly object soundGate = new();
    static readonly HashSet<string> sounded = [];
    static readonly Queue<string> soundHistory = [];
    static readonly Dictionary<string, long> soundChannels = [];
    static readonly Queue<string> soundChannelHistory = [];
    enum StationSound { Read, Success, Rejected }
    sealed record PendingSound(string Key, string Channel, StationSound Kind);
    PendingSound? pendingSound;
    readonly DispatcherTimer soundRetry = new();
    internal int PlayedReadSounds { get; private set; }
    internal int PlayedSuccessSounds { get; private set; }
    internal int PlayedRejectionSounds { get; private set; }
    string? rejectionShown, successShown;
    internal int SuccessCueCount { get; private set; }
    internal int ReadCueCount { get; private set; }
    internal int RejectionCueCount { get; private set; }
    string? feedbackShown, readMeasured, slotMeasured;
    bool measuring;
    EventHandler? compositionWaiter;
    internal string? CompositionFailure { get; private set; }
    long presentationGeneration;
    void InitializeCompositionFeedback()
    {
        soundRetry.Tick += (_, _) => TryPlayPendingSound();
        Loaded += (_, _) => ObservePresentation();
        Unloaded += (_, _) => { presentationGeneration++; pendingSound = null; soundRetry.Stop(); if (compositionWaiter is not null) { CompositionTarget.Rendering -= compositionWaiter; compositionWaiter = null; measuring = false; } };
    }
    void ObservePresentation()
    {
        if (!IsLoaded || !IsVisible || DataContext is not SupplyStationPositionViewModel p || Host?.ViewModel is not { Fresh: true } vm ||
            vm.Snapshot is not { } snapshot ||
            Window.GetWindow(this) is not { WindowState: not WindowState.Minimized }) return;
        string? id = null; string? stage = null;
        var soundChannel = $"{snapshot.RuntimeId}/{p.PositionId}";
        var rejected = false;
        if (p.RejectionIdentity is { } rejection && rejectionShown != rejection)
        {
            rejectionShown = rejection; RejectionCueCount++; rejected = true;
            OfferSound("Rejected/" + rejection, soundChannel, StationSound.Rejected);
        }
        if (p.SuccessIdentity is { } success && successShown != success)
        {
            successShown = success; SuccessCueCount++;
            OfferSound("Success/" + success, soundChannel, StationSound.Success);
        }
        if (p.ReadFeedbackIdentity is { } read && feedbackShown != p.ReadFeedbackKey && p.CameraReadFeedback.Length != 0)
        {
            feedbackShown = p.ReadFeedbackKey; ReadCueCount++;
            // A terminal result owns the sound if read and result coalesce into one render.
            if (!rejected && p.SuccessIdentity is null && p.RejectionIdentity is null) OfferSound("Read/" + p.ReadFeedbackKey, soundChannel, StationSound.Read);
        }
        if (vm.Application is not IParcelPresentationDiagnostics source) return;
        var readKey = p.ReadFeedbackKey;
        if (p.ReadFeedbackIdentity is { } current && readKey != readMeasured && p.CameraReadFeedback.Length != 0)
        { id = current; stage = "ReadFeedbackComposed"; }
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
                if (stage == "ReadFeedbackComposed" && (p.ReadFeedbackKey != readKey || p.CameraReadFeedback.Length == 0)) return;
                if (stage == "SlotComposed" && (p.Input?.Observation.Id != id || !p.ShowConfirmedTask)) return;
                await source.ReportPresentationAsync(snapshot.RuntimeId, id, stage!, Stopwatch.GetTimestamp(), stage == "SlotComposed" ? snapshot.PublishedTicks : 0, CancellationToken.None);
                if (stage == "SlotComposed") slotMeasured = id;
                else readMeasured = readKey;
            }
            catch (Exception error) { CompositionFailure = error.GetType().Name + ": " + error.Message; }
            finally
            {
                measuring = false;
                // A newer read may replace this one before composition. Measure that
                // current identity once; showing its cue again is independently deduped.
                if (generation == presentationGeneration && readKey != p.ReadFeedbackKey) ObservePresentation();
            }
        };
        compositionWaiter = rendered; CompositionTarget.Rendering += rendered;
        InvalidateVisual();
    }
    void OfferSound(string key, string channel, StationSound kind)
    {
        pendingSound = new(key, channel, kind); TryPlayPendingSound();
    }
    void TryPlayPendingSound()
    {
        soundRetry.Stop();
        if (pendingSound is not { } sound) return;
        var current = IsLoaded && IsVisible && Host?.ViewModel.Fresh == true && DataContext is SupplyStationPositionViewModel p &&
            sound.Key == (sound.Kind switch
            {
                StationSound.Success when p.SuccessIdentity is { } identity => "Success/" + identity,
                StationSound.Rejected when p.RejectionIdentity is { } identity => "Rejected/" + identity,
                StationSound.Read when p.CameraReadFeedback.Length > 0 && p.SuccessIdentity is null && p.RejectionIdentity is null => "Read/" + p.ReadFeedbackKey,
                _ => null
            });
        if (!current) { pendingSound = null; return; }
        var wait = ClaimSound(sound);
        if (wait > 0) { soundRetry.Interval = TimeSpan.FromMilliseconds(Math.Max(10, wait)); soundRetry.Start(); return; }
        pendingSound = null;
        if (wait < 0) return;
        switch (sound.Kind)
        {
            case StationSound.Read: PlayedReadSounds++; Host?.PlayCue(OperatorCue.Read, ((SupplyStationPositionViewModel)DataContext).PositionId); break;
            case StationSound.Success: PlayedSuccessSounds++; Host?.PlayCue(OperatorCue.Success, ((SupplyStationPositionViewModel)DataContext).PositionId); break;
            case StationSound.Rejected: PlayedRejectionSounds++; Host?.PlayCue(OperatorCue.Rejected, ((SupplyStationPositionViewModel)DataContext).PositionId); break;
        }
    }
    static double ClaimSound(PendingSound sound)
    {
        lock (soundGate)
        {
            if (sounded.Contains(sound.Key)) return -1;
            var now = Stopwatch.GetTimestamp();
            var group = sound.Channel + "/" + sound.Kind;
            if (soundChannels.TryGetValue(group, out var previous))
            {
                var remaining = (sound.Kind == StationSound.Rejected ? 500 : 1000) - Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;
                if (remaining > 0) return remaining;
            }
            else
            {
                if (soundChannelHistory.Count == 128) soundChannels.Remove(soundChannelHistory.Dequeue());
                soundChannelHistory.Enqueue(group);
            }
            soundChannels[group] = now;
            if (soundHistory.Count == 512) sounded.Remove(soundHistory.Dequeue());
            sounded.Add(sound.Key); soundHistory.Enqueue(sound.Key);
            return 0;
        }
    }
}
