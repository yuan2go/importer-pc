using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Same-process observation only. It neither repairs history nor changes UTC facts.
internal static class ParcelReceiptChronology
{
    public static StageReply Observe(FrozenRequest request, StageReply reply, Guid runtimeId,
        DateTimeOffset started, DateTimeOffset completed, TimeSpan elapsed, int budgetMs, StageReply? original = null)
    {
        if (reply.LocalClockEvidence is not null) throw new InvalidDataException("Provider cannot supply Application clock evidence");
        // Observation metadata is owned by the first durable receipt. Re-observing
        // exactly the same provider facts must not invent another reply identity.
        // A changed provider field still flows to the original conflict guards.
        if (original is not null && Wire.Encode(original with { LocalClockEvidence = null }) == Wire.Encode(reply)) return original;
        if (completed >= started) return reply;
        var proof = new ReceiptClockEvidence(1, runtimeId, request.Id, request.PreparedAt, started, completed, elapsed.Ticks, budgetMs, Wire.Hash(Wire.Encode(reply)));
        return reply with { LocalClockEvidence = proof };
    }
    public static bool Valid(FrozenRequest request, StageReply reply, DateTimeOffset now)
    {
        if (reply.LocalClockEvidence is not { } proof) return reply.At >= request.PreparedAt && reply.At <= now.AddSeconds(1);
        if (proof.Schema != 1 || proof.RuntimeId == Guid.Empty || proof.RequestId != request.Id || proof.PreparedAt != request.PreparedAt ||
            proof.StartedAt < request.PreparedAt || proof.CompletedAt >= proof.StartedAt || proof.BudgetMilliseconds is < 100 or > 30000 ||
            proof.ElapsedTicks < 0 || proof.ElapsedTicks > TimeSpan.FromMilliseconds(proof.BudgetMilliseconds + 1000).Ticks ||
            proof.ReplySha256 != Wire.Hash(Wire.Encode(reply with { LocalClockEvidence = null }))) return false;
        var span = TimeSpan.FromTicks(proof.ElapsedTicks).Add(TimeSpan.FromSeconds(1));
        // A returned local timestamp must be close to a witnessed endpoint. A
        // rollback is not a blanket waiver for an arbitrary old/future reply.
        return reply.At >= proof.CompletedAt - span && reply.At <= proof.CompletedAt.AddSeconds(1) ||
            reply.At >= proof.StartedAt.AddSeconds(-1) && reply.At <= proof.StartedAt + span;
    }
}
