using Ipc.Application.Contracts;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    internal static async Task ComponentBindingChecks(string workspace)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var peer = new Peer { Mode = "assigned" };
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" };
        const string applicationId = "synthetic-other-product";
        var configuration = HeartbeatConfiguration(workspace, binding) with { ApplicationId = applicationId };
        await using var service = new SportmasterRcsTaskService(binding);
        Reject(() => service.ConfigureRuntime(configuration with { Wcs = null }));
        Reject(() => service.ConfigureRuntime(configuration with { Wcs = binding with { Version = "1.0.1" } }));
        service.ConfigureRuntime(configuration);
        await service.StartAsync(budget.Token);
        Check(await service.SendHeartbeatAsync(budget.Token) == RcsHeartbeatResult.Received,
            "the component confirms a real loopback heartbeat for another product with the same effective binding");

        var request = Request(binding) with { ApplicationId = applicationId };
        Reject(() => service.PrepareRequest(request with { ApplicationId = " " }, 17));
        Reject(() => service.PrepareRequest(request with { Stage = "plc" }, 17));
        Reject(() => service.PrepareRequest(request with { Binding = binding with { Version = "1.0.1" } }, 17));
        Reject(() => service.PrepareRequest(request with { PayloadHash = Wire.Hash("different-payload") }, 17));
        var frozen = service.PrepareRequest(request, 17);
        var original = request with { IntegrationRequest = frozen };
        Check((await service.ResolveSlotAsync(original, budget.Token)).BusinessAccepted == true &&
            NonHeartbeatRequests(peer).Single().Body == frozen.Body,
            "another product sends its exact frozen wire intent once, with application, stage, binding and hash still validated");

        static void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("RCS accepted an invalid binding, stage, identity or payload hash");
        }
    }

    internal static async Task ComponentDispatchChecks()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = budget.Token;
        var slots = new SportmasterRcsTaskService.RcsSendSlots();
        var held = await Fill("main");
        try
        {
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            var first = slots.AcquireAsync("main", token);
            var removed = slots.AcquireAsync("main", cancelled.Token);
            var rest = Enumerable.Range(0, 126).Select(_ => slots.AcquireAsync("main", token)).ToArray();
            Check(slots.Snapshot() is { Capacity: 7, Occupied: 7, ActiveSends: 0 } && !first.IsCompleted && !rest[^1].IsCompleted,
                "one arbitrary position keeps the seven-connection ceiling under a full waiting queue");
            try
            {
                using var unexpected = await slots.AcquireAsync("main", token);
                throw new InvalidOperationException("RCS accepted a 129th waiter in one position");
            }
            catch (IOException) { }
            await cancelled.CancelAsync();
            try
            {
                using var unexpected = await removed;
                throw new InvalidOperationException("Cancelled waiter unexpectedly received a slot");
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            var replacement = slots.AcquireAsync("main", token);
            held[0].Dispose();
            held[0].Dispose();
            using (await first) Check(!rest[0].IsCompleted && slots.Snapshot().Occupied == 7,
                "a single release grants only the first waiter; cancellation removed its exact neighbor");
            foreach (var waiting in rest)
            {
                using var granted = await waiting;
                CheckOccupied();
            }
            using (await replacement) Check(slots.Snapshot().Occupied == 7, "cancellation returned one waiting place and FIFO order was preserved");
        }
        finally { foreach (var slot in held) slot.Dispose(); }
        Check(slots.Snapshot().Occupied == 0, "single-position sends and waiters release all reservations");

        held = await Fill("left");
        try
        {
            var left1 = slots.AcquireAsync("left", token);
            var left2 = slots.AcquireAsync("left", token);
            var right1 = slots.AcquireAsync("right", token);
            var right2 = slots.AcquireAsync("right", token);
            Check(slots.Snapshot() is { Occupied: 7, LeftWaiting: 2, RightWaiting: 2 }, "two positions retain their legacy waiting counters");
            held[0].Dispose();
            using (await right1) Check(!left1.IsCompleted && !right2.IsCompleted, "right gets the first free slot after left");
            using (await left1) Check(!right2.IsCompleted && !left2.IsCompleted, "left takes its turn before the second right waiter");
            using (await right2) Check(!left2.IsCompleted, "right and left alternate while both are queued");
            using (await left2) Check(slots.Snapshot() is { Occupied: 7, LeftWaiting: 0, RightWaiting: 0 }, "each legacy queue also preserves its local FIFO order");
        }
        finally { foreach (var slot in held) slot.Dispose(); }

        held = await Fill("holding");
        try
        {
            var main1 = slots.AcquireAsync("main", token);
            var main2 = slots.AcquireAsync("main", token);
            var middle = slots.AcquireAsync("middle", token);
            var east = slots.AcquireAsync("east", token);
            held[0].Dispose();
            using (await main1) Check(!middle.IsCompleted && !east.IsCompleted, "three independent queues admit only one waiter per freed slot");
            using (await middle) Check(!main2.IsCompleted && !east.IsCompleted, "the second position precedes the first position's remaining wait");
            using (await east) Check(!main2.IsCompleted, "the third position gets a turn before the first position repeats");
            using (await main2) CheckOccupied();
        }
        finally { foreach (var slot in held) slot.Dispose(); }
        Check(slots.Snapshot() is { Occupied: 0, ActiveSends: 0, LeftWaiting: 0, RightWaiting: 0 }, "every position releases its permits to the common pool");

        async Task<IDisposable[]> Fill(string position)
        {
            var permits = new IDisposable[7];
            for (var i = 0; i < permits.Length; i++) permits[i] = await slots.AcquireAsync(position, token);
            return permits;
        }

        void CheckOccupied()
        {
            if (slots.Snapshot().Occupied != 7) throw new InvalidOperationException("RCS changed the occupied count while rotating a permit");
        }
    }
}
