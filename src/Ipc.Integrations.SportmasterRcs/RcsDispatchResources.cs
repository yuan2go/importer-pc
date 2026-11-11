using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

public sealed partial class SportmasterRcsTaskService : IWcsDispatchResources
{
    // Preserve the original eight-connection ceiling: seven destination calls
    // plus an independent one-connection heartbeat pool.
    internal const int MaximumDestinationSends = 7;
    readonly RcsSendSlots sendSlots = new();
    public WcsDispatchProgress DispatchProgress
    {
        get
        {
            lock (lifecycle) return sendSlots.Snapshot() with
            {
                Running = running && !disposed,
                RemoteConfirmed = running && !disposed && heartbeatResponseReceived && heartbeatFailure is null &&
                    heartbeatReceivedAt <= HeartbeatTimeForVerification.GetUtcNow() && heartbeatValidUntil > HeartbeatTimeForVerification.GetUtcNow(),
                RemoteValidUntil = heartbeatResponseReceived ? heartbeatValidUntil : null
            };
        }
    }
    public async Task<IWcsDispatchPermit> ReserveDispatchAsync(FrozenRequest request, CancellationToken token)
    {
        var owned = Wire.Decode<FrozenRequest>(Wire.Encode(request));
        var body = ValidateDestination(owned);
        var position = Wire.Decode<ParcelPayload>(owned.Payload).PositionId;
        if (position is not ("left" or "right")) throw new InvalidDataException("RCS destination requires an actual station position");
        CancellationToken stop;
        lock (lifecycle)
        {
            if (!running || disposed) throw new IOException("RCS client is stopped");
            stop = heartbeatStop!.Token;
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, stop);
            budget.CancelAfter(options.RequestTimeoutMs);
            var slot = await sendSlots.AcquireAsync(position, budget.Token).ConfigureAwait(false);
            return new DispatchPermit(this, owned, body, slot);
        }
        catch { DispatchReleased(); throw; }
    }
    void DispatchReleased() { lock (lifecycle) if (--active == 0) drained!.TrySetResult(); }
    sealed class DispatchPermit(SportmasterRcsTaskService owner, FrozenRequest request, string body, IDisposable slot) : IWcsDispatchPermit
    {
        int state; // reserved / sending / complete / released without sending
        public async Task<StageReply> SendAsync(CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) throw new InvalidOperationException("RCS send permit is single use");
            owner.sendSlots.BeginSend();
            try { return await owner.SendDestinationAsync(request, body, token).ConfigureAwait(false); }
            finally { owner.sendSlots.EndSend(); Interlocked.Exchange(ref state, 2); slot.Dispose(); owner.DispatchReleased(); }
        }
        public void Dispose()
        {
            // Disposing an in-flight permit cannot publish a free slot while the
            // original SendAsync still owns its HTTP response/body.
            if (Interlocked.CompareExchange(ref state, 3, 0) == 0) { slot.Dispose(); owner.DispatchReleased(); }
        }
    }
    sealed class RcsSendSlots
    {
        readonly object sync = new();
        readonly LinkedList<Waiter> left = new(), right = new();
        int occupied, sending;
        bool rightNext;
        sealed class Waiter(string position)
        {
            public readonly string Position = position;
            public readonly TaskCompletionSource<IDisposable> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public LinkedListNode<Waiter>? Node;
        }
        public WcsDispatchProgress Snapshot()
        { lock (sync) return new(MaximumDestinationSends, occupied, sending, left.Count, right.Count); }
        public void BeginSend() { lock (sync) sending++; }
        public void EndSend() { lock (sync) sending--; }
        public async Task<IDisposable> AcquireAsync(string position, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var waiter = new Waiter(position);
            lock (sync)
            {
                var queue = position == "left" ? left : right;
                if (queue.Count >= 128) throw new IOException("RCS send resource waiting capacity is exhausted");
                waiter.Node = queue.AddLast(waiter); Grant();
            }
            using var cancelled = token.Register(() =>
            {
                lock (sync)
                {
                    if (waiter.Node is not { } node) return;
                    (waiter.Position == "left" ? left : right).Remove(node); waiter.Node = null;
                    waiter.Ready.TrySetCanceled(token);
                }
            });
            var slot = await waiter.Ready.Task.ConfigureAwait(false);
            if (token.IsCancellationRequested) { slot.Dispose(); token.ThrowIfCancellationRequested(); }
            return slot;
        }
        void Grant()
        {
            while (occupied < MaximumDestinationSends && (left.Count != 0 || right.Count != 0))
            {
                var queue = rightNext && right.Count != 0 || left.Count == 0 ? right : left;
                var waiter = queue.First!.Value; queue.RemoveFirst(); waiter.Node = null;
                rightNext = waiter.Position == "left"; occupied++;
                waiter.Ready.SetResult(new Slot(this));
            }
        }
        void Release() { lock (sync) { occupied--; Grant(); } }
        sealed class Slot(RcsSendSlots owner) : IDisposable
        {
            RcsSendSlots? held = owner;
            public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
        }
    }
}
