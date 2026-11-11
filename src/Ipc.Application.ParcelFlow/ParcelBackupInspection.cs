using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
using System.Reflection;

namespace Ipc.Application.ParcelFlow;

public sealed record ParcelBackupInspection(string Status, string Application, string Station, RunMode Mode, int DatabaseVersion,
    string DatabaseSha256, string ManifestSha256, long RequiredBytes, long MinimumFreeBytes, string VerifierSourceIdentity);

public static partial class ParcelRecovery
{
    // Shared by offline analysis and its CLI. No repository writable open, restore,
    // runtime or device activation; the format and custody rules have one owner.
    public static ParcelBackupInspection InspectAnalysisBackup(string source, FlowDefinition flow, CancellationToken token = default)
    {
        source = Path.GetFullPath(source);
        RecoveryFiles.RequireOrdinaryAncestors(source);
        var manifest = VerifyBackup(source, token: token);
        if (manifest.Application != flow.Id) throw new InvalidDataException("备份所属业务不符");
        var root = source + ".recovery";
        var config = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(RecoveryFiles.Resolve(root, "configuration.json")));
        if (config.Effective.ApplicationId != manifest.Application || flow.Validate(config.Effective).Length != 0 ||
            config.Pending is { } pending && (flow.Validate(pending).Length != 0 || pending.ApplicationId != manifest.Application ||
                pending.StationId != config.Effective.StationId || pending.Mode != config.Effective.Mode))
            throw new InvalidDataException("备份配置身份或当前格式不符");
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new Uri(source).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            db.Open();
            SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
            using var command = db.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
            using var rows = command.ExecuteReader();
            if (!rows.Read() || rows.GetString(0) != "ok" || rows.Read()) throw new InvalidDataException("备份数据库完整性核验失败");
        }
        token.ThrowIfCancellationRequested();
        var manifestFile = Path.Combine(root, "COMPLETED.json");
        var bytes = checked(new FileInfo(source).Length + new FileInfo(manifestFile).Length + manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(root, f.Key)).Length));
        return new("BackupVerified", manifest.Application, config.Effective.StationId, config.Effective.Mode, ParcelRepository.CurrentStorageVersion,
            manifest.DatabaseHash, Wire.FileHash(manifestFile, token), bytes, config.Effective.Retention?.MinimumFreeBytes ?? 67108864,
            typeof(ParcelRepository).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unidentified source");
    }
}
