using Ipc.Platform;

namespace Ipc.Supply;

internal sealed record ReceiptIngress(string Id, string RequestId, SendAttempt? Attempt, IntegrationResponse Response, DateTimeOffset ReceivedAt);

// One validator is shared by read-only startup preflight and the owned recovery pass.
internal static class ReceiptInputs
{
    internal static void RequireNoFence(string database)
    {
        var fence = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(database)!, "receipt-owner-fence.json");
        try { _ = File.GetAttributes(fence); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new InvalidDataException("Unsupported receipt fence; original file and request responsibility retained");
    }
    internal static string[] Files(string database)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(database)!, "receipt-ingress");
        try { return Directory.GetFiles(directory); }
        catch (DirectoryNotFoundException) { return []; }
    }
    internal static void Validate(SupplyStore store, IRequestContract contract)
    {
        RequireNoFence(store.Path);
        foreach (var file in Files(store.Path)) _ = Read(store, contract, file);
    }
    internal static (ReceiptIngress Ingress, BusinessRequest Original, ReceiptResponsibility Occurrence) Read(SupplyStore store, IRequestContract contract, string file)
    {
        var ingress = Wire.Decode<ReceiptIngress>(File.ReadAllText(file));
        if (System.IO.Path.GetFileName(file) != ingress.Id + ".json") throw new InvalidDataException("Interrupted receipt custody requires inspection; original retained");
        var original = store.Request(ingress.RequestId) ?? throw new InvalidDataException("Receipt custody has no original request");
        contract.ValidateResponse(original, ingress.Response);
        var occurrence = store.ReceiptOccurrence(ingress.Id) ?? throw new InvalidDataException("Receipt occurrence reservation missing; raw original retained without replay");
        if (occurrence.RequestId != original.Id || occurrence.AttemptId != ingress.Attempt?.Id ||
            occurrence.Status is not (ReceiptResponsibilityRules.AwaitingResponse or ReceiptResponsibilityRules.RawUnavailable or ReceiptResponsibilityRules.Committed) ||
            occurrence.Status == ReceiptResponsibilityRules.Committed && (occurrence.Response != ingress.Response || occurrence.ContentSha256 != Wire.Hash(Wire.Encode(ingress.Response))))
            throw new InvalidDataException("Receipt reservation or committed raw identity differs; originals retained");
        if (ingress.Attempt is { } attempt)
        {
            var stored = store.Attempts(original.Id).SingleOrDefault(x => x.Id == attempt.Id);
            if (stored is null || stored.RequestId != attempt.RequestId || stored.Ordinal != attempt.Ordinal || stored.StartedAt != attempt.StartedAt)
                throw new InvalidDataException("Receipt original send attempt identity differs; originals retained");
        }
        return (ingress, original, occurrence);
    }
}
