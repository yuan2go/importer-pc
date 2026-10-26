using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record ArchiveCount(long Revision, long Count);
    ArchiveCount? archiveCount;
    readonly object archiveCountOwnership = new();
    Task<long>? archiveCountRead;
    long archiveCountReadRevision = -1;
    async Task RefreshArchiveCountAsync()
    {
        var original = repository ?? throw new InvalidOperationException("Application has not started");
        while (true)
        {
            var revision = original.ArchiveRevision;
            if (Volatile.Read(ref archiveCount)?.Revision == revision) return;
            Task<long> read;
            lock (archiveCountOwnership)
            {
                if (archiveCountRead is null || archiveCountReadRevision != revision || archiveCountRead.IsFaulted || archiveCountRead.IsCanceled)
                {
                    archiveCountReadRevision = revision;
                    archiveCountRead = OwnedRead(token => Task.FromResult(original.ReadArchivedCount(token)));
                }
                read = archiveCountRead;
            }
            var count = await read.ConfigureAwait(false);
            if (original.ArchiveRevision != revision) continue;
            Volatile.Write(ref archiveCount, new(revision, count)); return;
        }
    }
    long operationProjectionRevision = -1;
    bool operationProjectionAuthorized;
    string? operationProjectionJson, snapshotHeaderJson, snapshotEncodedJson;
    string EncodeSnapshot(ParcelSnapshot projection)
    {
        var authorized = Management.Status().Authorized;
        if (operationProjectionJson is null || operationProjectionRevision != projection.Revision || operationProjectionAuthorized != authorized)
        {
            Boundary?.Invoke("SnapshotEncoding");
            operationProjectionJson = Wire.Encode(authorized ? projection.Operations : projection.Operations.Select(OperatorOperation).ToArray());
            operationProjectionRevision = projection.Revision; operationProjectionAuthorized = authorized;
            snapshotHeaderJson = null;
        }
        // Health expiry, operator authority, workers and diagnostics are rebuilt on
        // every read; only the expensive immutable business originals share revision.
        var header = Wire.Encode(OperatorSnapshot(projection with { Operations = [] }, authorized));
        if (header == snapshotHeaderJson && snapshotEncodedJson is not null) return snapshotEncodedJson;
        using var document = JsonDocument.Parse(header);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(nameof(ParcelSnapshot.Operations)))
                { writer.WritePropertyName(property.Name); writer.WriteRawValue(operationProjectionJson, skipInputValidation: true); }
                else property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        snapshotHeaderJson = header; snapshotEncodedJson = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        return snapshotEncodedJson;
    }
}
