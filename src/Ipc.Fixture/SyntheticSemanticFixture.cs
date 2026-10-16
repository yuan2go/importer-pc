using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Fixture;

// Independent, explicitly synthetic task source. These fields describe no real WCS protocol
// or approved production allocation rule. Identity keys, rather than the UI or current box,
// determine each item's wave/task; the receiver persists the resulting receipt unchanged.
public static class SyntheticSemanticFixture
{
    public const string Source = "Independent synthetic task fixture";
    public const string Version = "SYNTHETIC-SEMANTICS-v1";
    static string Key(string identity) => Wire.Hash(identity)[..12].ToUpperInvariant();
    public static ContextSnapshot Context(FixturePayload payload, string evidenceId, DateTimeOffset acquiredAt) =>
        new("SIM-BOX-WAVE-" + Key(payload.SessionId), new(Source, Version, evidenceId, acquiredAt));
    public static TaskSnapshot Item(FixturePayload payload, string evidenceId, DateTimeOffset acquiredAt) =>
        new("SIM-SKU-" + Key(payload.Barcode), "SIM-ITEM-WAVE-" + Key(payload.OwnerId),
            "SIM-TASK-" + Key(payload.SessionId + ":" + payload.OwnerId), "SIM-CHUTE-" + (Convert.ToUInt32(Key(payload.OwnerId)[..4], 16) % 12 + 1).ToString("00"),
            "件", new(Source, Version, evidenceId, acquiredAt));
}
