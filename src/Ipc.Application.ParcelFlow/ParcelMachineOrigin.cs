using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Recovery custody only: this cannot choose a configuration, deployment or device.
// Never issue a fresh machine claim for a pre-existing ledger/database.
internal static class ParcelMachineOrigin
{
    internal const string FileName = "machine-origin.bin";
    sealed record Origin(int SchemaVersion, string Application, string Station, RunMode Mode, string Directory, string Id, bool PortableSynthetic);
    internal static void EnsureNew(string directory, ParcelConfiguration c)
    {
        var file = Path.Combine(directory, FileName);
        RecoveryFiles.RequireOrdinaryAncestors(file);
        if (File.Exists(file)) { Require(directory, c); return; }
        if (File.Exists(Path.Combine(directory, c.ApplicationId + ".db")) || File.Exists(Path.Combine(directory, "configuration.json"))) return;
        ProtectedDraftPayload.Write(file, Wire.Encode(new Origin(1, c.ApplicationId, c.StationId, c.Mode, Path.GetFullPath(directory), Wire.Id(), !OperatingSystem.IsWindows())), c.Mode == RunMode.Simulation);
        Require(directory, c);
    }
    internal static void Require(string directory, ParcelConfiguration c) => Require(directory, c, new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    static void Require(string directory, ParcelConfiguration c, HashSet<string> visited)
    {
        directory = Path.GetFullPath(directory);
        if (visited.Count == 16 || !visited.Add(directory)) throw new InvalidDataException("原机器来源接管链循环或超过16级预算；保留原件核对");
        var file = Path.Combine(directory, FileName);
        if (!File.Exists(file)) throw new InvalidDataException("缺少原机器来源保管证据；不能从旧库/复制根补盖本机证明。完整备份与隔离恢复仍可用，原机自动接管保持BLOCKED");
        try
        {
            var origin = Wire.Decode<Origin>(ProtectedDraftPayload.Read(file));
            if (origin.SchemaVersion != 1 || origin.Application != c.ApplicationId || origin.Station != c.StationId || origin.Mode != c.Mode || !Guid.TryParseExact(origin.Id, "N", out _) ||
                origin.PortableSynthetic != !OperatingSystem.IsWindows() || origin.PortableSynthetic && c.Mode != RunMode.Simulation)
                throw new InvalidDataException("原机器来源或原目录身份不符；保留隔离，不能将复制目录当成原机");
            if (!string.Equals(origin.Directory, directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                // A later legitimate migration keeps the original ciphertext; prove
                // its already-fenced lineage instead of issuing a new machine claim.
                static string Read(string path)
                { RecoveryFiles.RequireOrdinaryAncestors(path); if (new FileInfo(path).Length > 65536) throw new InvalidDataException("来源接管链证据超出预算"); return File.ReadAllText(path); }
                var proof = Wire.Decode<StartupRestoreHandoff>(Read(Path.Combine(directory, "restore-handoff.json")));
                if (proof.TargetDirectory != directory || !Path.IsPathFullyQualified(proof.SourceDirectory) || proof.SourceDirectory.StartsWith(@"\\", StringComparison.Ordinal) ||
                    Wire.Encode(proof) != Wire.Encode(Wire.Decode<StartupRestoreHandoff>(Read(Path.Combine(proof.SourceDirectory, "MIGRATION-SOURCE-FENCED.json")))) ||
                    Wire.FileHash(file) != Wire.FileHash(Path.Combine(proof.SourceDirectory, FileName)))
                    throw new InvalidDataException("缺少与原机器密文一致的已封存接管链，不能将复制目录当成原机");
                Require(proof.SourceDirectory, c, visited);
            }
        }
        catch (System.ComponentModel.Win32Exception error)
        { throw new InvalidDataException("无法验证原机器DPAPI来源；换机恢复保持隔离，须取得真实来源退役/秘密/设备/编号交接证据", error); }
    }
}
