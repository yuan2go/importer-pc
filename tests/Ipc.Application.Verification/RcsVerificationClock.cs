// Controlled request/heartbeat time shared by real adapter and native UI fixtures.
static partial class SportmasterRcs
{
    internal sealed class RequestClock : TimeProvider
    {
        readonly object sync = new();
        readonly List<RequestTimer> timers = [];
        long ticks;
        internal int PendingTimers { get { lock (sync) return timers.Count(t => !t.Disposed && t.Due != long.MaxValue); } }
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
