using Microsoft.Data.Sqlite;

namespace Ipc.Supply;

// Detached startup facts; no writable store escapes the read-only selection pass.
public sealed record SupplyStartupState(ConfigRevision? Effective, ConfigRevision? Pending,
    StationState State, int PendingResponsibilities);

public sealed partial class SupplyStore
{
    SupplyStore(string path, SqliteConnection readOnly) { Path = path; db = readOnly; }

    public static SupplyStartupState ReadStartupState(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        ReceiptInputs.RequireNoFence(path);
        if (!File.Exists(path))
        {
            if (ReceiptInputs.Files(path).Length != 0) throw new InvalidDataException("Raw receipt has no original database; originals retained");
            return new(null, null, new(), 0);
        }
        using var view = new SupplyStore(path, OpenMaintenance(path, SqliteOpenMode.ReadOnly));
        _ = InspectConnection(view.db, path);
        return new(view.CurrentConfig("Effective"), view.CurrentConfig("PendingActivation"), view.Load(), view.PendingCount());
    }

    static bool ValidateBeforeWrite(string path, IRequestContract? contract)
    {
        ReceiptInputs.RequireNoFence(path);
        var files = ReceiptInputs.Files(path);
        var existing = File.Exists(path);
        if (files.Length != 0 && !existing) throw new InvalidDataException("Raw receipt has no original database; originals retained");
        if (files.Length != 0 && contract is null) throw new InvalidDataException("Raw receipt requires its original request contract before opening a writer");
        if (!existing) return false;
        using var view = new SupplyStore(path, OpenMaintenance(path, SqliteOpenMode.ReadOnly));
        _ = InspectConnection(view.db, path);
        foreach (var file in files) _ = ReceiptInputs.Read(view, contract!, file);
        return true;
    }
}
