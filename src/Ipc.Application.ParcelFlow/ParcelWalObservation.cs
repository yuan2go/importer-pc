using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    string? walObservation;
    long walHighWaterBytes, previousWalBacklog;
    internal TimeSpan? WalIntervalForVerification { get; set; }
    async Task ObserveWalAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    var file = Path.Combine(directory!, Id + ".db");
                    static long Bytes(string path) { var info = new FileInfo(path); return info.Exists ? info.Length : 0; }
                    var before = Bytes(file + "-wal"); walHighWaterBytes = Math.Max(walHighWaterBytes, before);
                    using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 1 }.ToString());
                    db.Open();
                    using var command = db.CreateCommand(); command.CommandText = "PRAGMA busy_timeout=1000;"; command.ExecuteNonQuery();
                    stopping.Token.ThrowIfCancellationRequested(); command.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
                    int busy, log, checkpointed;
                    using (var row = command.ExecuteReader())
                    {
                        if (!row.Read()) throw new InvalidDataException("PASSIVE checkpoint returned no observation");
                        busy = row.GetInt32(0); log = row.GetInt32(1); checkpointed = row.GetInt32(2);
                    }
                    var backlog = Math.Max(0L, (long)log - checkpointed); var after = Bytes(file + "-wal");
                    walHighWaterBytes = Math.Max(walHighWaterBytes, after);
                    var warning = after > (Retention.WalWarningBytes ?? 67108864);
                    var observation = new { At = DateTimeOffset.UtcNow, BytesBefore = before, BytesAfter = after, HighWaterBytes = walHighWaterBytes,
                        Busy = busy, LogFrames = log, CheckpointedFrames = checkpointed, BacklogFrames = backlog,
                        LogicalReclaimed = previousWalBacklog > 0 && backlog == 0, Warning = warning,
                        Policy = "PASSIVE only; checkpointed WAL can be reused without physical truncation; never delete active WAL" };
                    previousWalBacklog = backlog;
                    var encoded = Wire.Encode(observation); Volatile.Write(ref walObservation, "WalCheckpoint:" + encoded);
                    Diagnostic("StorageWal", "WalCheckpoint", warning ? "警告" : "信息", encoded);
                    backgroundProgress["wal-checkpoint"] = (DateTimeOffset.UtcNow, warning ? "Warning:WAL high water" : "Observed");
                }
                catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException)
                {
                    Volatile.Write(ref walObservation, "WalCheckpoint:Unavailable:" + error.GetType().Name);
                    Diagnostic("StorageWal", "WalCheckpointUnavailable", "警告", error.GetType().Name);
                    backgroundProgress["wal-checkpoint"] = (DateTimeOffset.UtcNow, "Unavailable:" + error.GetType().Name);
                }
                await Task.Delay(WalIntervalForVerification ?? TimeSpan.FromSeconds(Retention.WalCheckpointSeconds ?? 60), stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }
}
