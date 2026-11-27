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
        var position = Wire.Decode<ParcelPayload>(owned.Payload).PositionId!;
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
            await InstallationResourceBoundary.RequireRcsEndpointAsync(options.Endpoint, budget.Token).ConfigureAwait(false);
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
    internal sealed class RcsSendSlots
    {
        const int MaximumWaitingPerPosition = 128;
        readonly object sync = new();
        readonly Dictionary<string, LinkedListNode<PositionQueue>> queues = new(StringComparer.Ordinal);
        readonly LinkedList<PositionQueue> rotation = new();
        int occupied, sending;
        string lastGranted = "right"; // On the existing two-position station, prefer left before the first grant.

        sealed class PositionQueue(string position)
        {
            public string Position { get; } = position;
            public LinkedList<Waiter> Waiters { get; } = new();
        }

        sealed class Waiter
        {
            public readonly TaskCompletionSource<IDisposable> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public LinkedListNode<Waiter>? Node;
        }

        public WcsDispatchProgress Snapshot()
        {
            lock (sync) return new(MaximumDestinationSends, occupied, sending,
                queues.GetValueOrDefault("left")?.Value.Waiters.Count ?? 0,
                queues.GetValueOrDefault("right")?.Value.Waiters.Count ?? 0);
        }

        public void BeginSend() { lock (sync) sending++; }
        public void EndSend() { lock (sync) sending--; }
        public async Task<IDisposable> AcquireAsync(string position, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(position);
            token.ThrowIfCancellationRequested();
            var waiter = new Waiter();
            LinkedListNode<PositionQueue> queue;
            lock (sync)
            {
                if (!queues.TryGetValue(position, out var pending))
                {
                    pending = rotation.AddLast(new PositionQueue(position));
                    queues.Add(position, pending);
                }
                queue = pending;
                if (queue.Value.Waiters.Count >= MaximumWaitingPerPosition)
                    throw new IOException("RCS send resource waiting capacity is exhausted");
                waiter.Node = queue.Value.Waiters.AddLast(waiter);
                Grant();
            }
            using var cancelled = token.Register(() =>
            {
                lock (sync)
                {
                    if (waiter.Node is null) return;
                    RemoveWaiter(queue, waiter);
                    waiter.Ready.TrySetCanceled(token);
                }
            });
            var slot = await waiter.Ready.Task.ConfigureAwait(false);
            if (token.IsCancellationRequested) { slot.Dispose(); token.ThrowIfCancellationRequested(); }
            return slot;
        }

        void RemoveWaiter(LinkedListNode<PositionQueue> queue, Waiter waiter)
        {
            queue.Value.Waiters.Remove(waiter.Node!);
            waiter.Node = null;
            if (queue.Value.Waiters.Count != 0) return;
            rotation.Remove(queue);
            queues.Remove(queue.Value.Position);
        }

        void Grant()
        {
            while (occupied < MaximumDestinationSends && rotation.First is { } first)
            {
                // The last served position may have rejoined an empty rotation before its peer.
                var queue = first.Value.Position == lastGranted && first.Next is { } next ? next : first;
                var waiter = queue.Value.Waiters.First!.Value;
                RemoveWaiter(queue, waiter);
                if (queue.List is not null)
                {
                    rotation.Remove(queue);
                    rotation.AddLast(queue);
                }
                lastGranted = queue.Value.Position;
                occupied++;
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
