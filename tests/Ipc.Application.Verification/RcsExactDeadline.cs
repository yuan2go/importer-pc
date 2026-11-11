using System.Net.Sockets;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    static async Task ExactRequestDeadlineChecks(string workspace)
    {
        await using var peer = new Peer { Mode = "assigned" };
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" };
        binding = binding with { Parameters = new(binding.Parameters.Where(p => p.Key != "request-timeout-ms")) };
        var configuration = HeartbeatConfiguration(workspace, binding);
        Check(RcsRequestPolicy.TimeoutMs(binding) == 1000 && new SportmasterFlow().QueuedObservationTimeoutMs(configuration with { TimeoutMs = 1500 }) == 1500 &&
            new SportmasterFlow().QueuedObservationTimeoutMs(configuration with { TimeoutMs = 1500, Wcs = binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = "30000" } } }) == 1500 &&
            new SportmasterFlow().QueuedObservationTimeoutMs(configuration with { TimeoutMs = 500, ReadTimeoutMs = 2000 }) == 2000,
            "RCS defaults to 1000ms; input delivery stays independent of its duration and cannot shorten a legitimate read round");
        foreach (var outcome in new[] { "before", "connect-timeout", "body-timeout", "caller-cancel" })
        {
            var clock = new RequestClock();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var connect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var prefix = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var service = new SportmasterRcsTaskService(binding, async (context, token) =>
            {
                entered.TrySetResult(); await connect.Task.WaitAsync(token);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(context.DnsEndPoint, token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }) { RequestTimeForVerification = clock, ResponseBodyProgressForVerification = (_, bytes) => { if (bytes > 0) prefix.TrySetResult(); } };
            service.ConfigureRuntime(configuration); service.SetEnabledPositions([]); await service.StartAsync(default);
            var request = Request(binding); request = request with { IntegrationRequest = service.PrepareRequest(request, 1) };
            var original = Wire.Encode(request); var before = peer.Requests.Count;
            using var caller = new CancellationTokenSource();
            peer.Mode = outcome == "body-timeout" ? "body-timeout" : "assigned";
            var pending = service.ResolveSlotAsync(request, caller.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            clock.Advance(600);
            if (outcome == "body-timeout")
            {
                connect.TrySetResult();
                await prefix.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            clock.Advance(399);
            Check(!pending.IsCompleted, outcome + " remains active at 999ms after actual connection/body boundary entry");
            if (outcome == "before")
            {
                connect.TrySetResult();
                Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))).BusinessAccepted == true && peer.Requests.Count == before + 1,
                    "valid response at logical 999ms is accepted once using the original 1000ms default");
            }
            else if (outcome == "body-timeout")
            {
                clock.Advance(1);
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
                Check(result.BusinessAccepted is null && result.HttpResponse is { Complete: false } raw &&
                    Encoding.UTF8.GetString(Convert.FromBase64String(raw.BodyBase64)) == "{\"requestId\":" && peer.Requests.Count == before + 1,
                    "connection consumes 600ms; body expires at total 1000ms without restarting its timer, retaining the actual prefix and one POST");
            }
            else if (outcome == "caller-cancel")
            {
                caller.Cancel();
                try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Caller cancellation lost"); }
                catch (OperationCanceledException) { Check(peer.Requests.Count == before, "caller cancellation before connection stays cancellation, never a business rejection or retry"); }
            }
            else
            {
                clock.Advance(1);
                try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Connection deadline restarted"); }
                catch (TimeoutException) { Check(peer.Requests.Count == before, "connection waiting expires exactly at 1000ms without writing a body or retrying"); }
            }
            Check(Wire.Encode(request) == original, "deadline handling preserves the frozen request, payload, target and permanent protocol identity");
        }
    }
    sealed class RequestClock : TimeProvider
    {
        readonly object sync = new();
        readonly List<RequestTimer> timers = [];
        long ticks;
        public override DateTimeOffset GetUtcNow() { lock (sync) return DateTimeOffset.UnixEpoch.AddTicks(ticks); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { lock (sync) { var timer = new RequestTimer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer; } }
        public void Advance(int milliseconds)
        {
            RequestTimer[] due;
            lock (sync)
            {
                ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
                due = timers.Where(t => !t.Disposed && t.Due <= ticks).ToArray();
                foreach (var timer in due) timer.Due = long.MaxValue;
            }
            foreach (var timer in due) timer.Callback(timer.State);
        }
        sealed class RequestTimer(RequestClock clock, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal long Due;
            internal bool Disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("Request budget must be one-shot");
                lock (clock.sync) { if (Disposed) return false; Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks; return true; }
            }
            public void Dispose() { lock (clock.sync) { Disposed = true; clock.timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
