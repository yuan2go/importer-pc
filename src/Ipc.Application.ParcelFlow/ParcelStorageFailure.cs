using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IRuntimeFatalSource
{
    readonly TaskCompletionSource<RuntimeFatalFailure> fatalStorage = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<RuntimeFatalFailure> FatalFailure => fatalStorage.Task;
    static bool IsStorageError(Exception error) => error is StorageWriteFailure or SqliteException or IOException or UnauthorizedAccessException;
    void StorageFailed(Exception error, string phase, bool knownIntegrityFailure = false)
    {
        // InvalidData is included only at known storage boundaries. Never
        // classify a rejected operator command as corrupt storage.
        Interlocked.Exchange(ref accepting, 0);
        var kind = knownIntegrityFailure ? StorageFailureKind.Unrecoverable : error is StorageWriteFailure write ? write.Kind :
            AtomicStorageWrite.IsUnrecoverable(error) ? StorageFailureKind.Unrecoverable : StorageFailureKind.DurableOutcomeUncertain;
        var fatalState = "Fatal storage failure: " + phase + "; " + kind + "; " + error.GetType().Name;
        if (error is StorageWriteFailure budget) fatalState += "; attempts=" + budget.Attempts + "; elapsedMs=" + budget.ElapsedMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        // Keep existing worker/operator diagnostics and guards intact.
        Interlocked.CompareExchange(ref failure, fatalState, null);
        fatalStorage.TrySetResult(new("Storage." + phase + "." + kind, kind == StorageFailureKind.Unrecoverable,
            error, runtimeId.ToString(), string.Join(",", readers.Select(x => x.Key + ":" + x.Value.Epoch).Concat(scaleEpochs.Select(x => x.Key + ":" + x.Value))), fatalState + "; original=" + failure));
    }
}
