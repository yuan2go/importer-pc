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
        string? runtimeSid = null;
        if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")))
        {
            var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
            MachineRuntimePolicy.RequireProtectedEvidence(policy);
            runtimeSid = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policy)).RuntimeAccountSid;
        }
        ProtectedDraftPayload.Write(file, Wire.Encode(new Origin(1, c.ApplicationId, c.StationId, c.Mode, Path.GetFullPath(directory), Wire.Id(), !OperatingSystem.IsWindows())), c.Mode == RunMode.Simulation, runtimeSid);
        Require(directory, c);
    }
    internal static void Require(string directory, ParcelConfiguration c) => Require(directory, c, new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    internal static string RebindUnusedSimulationDirectory(string original, string target, ParcelConfiguration c, string expectedOriginHash, string? runtimeSid = null)
    {
        if (!OperatingSystem.IsWindows() || c.Mode != RunMode.Simulation) throw new InvalidOperationException("仅处理同机尚未供包的旧模拟目录；生产/已作业数据沿原完整恢复与接管流程。");
        var sourceFile = Path.Combine(original, FileName); var targetFile = Path.Combine(target, FileName);
        RecoveryFiles.RequireOrdinaryAncestors(sourceFile); RecoveryFiles.RequireOrdinaryAncestors(targetFile);
        if (Wire.FileHash(sourceFile) != expectedOriginHash) throw new InvalidDataException("原机器来源密文在核对后变化。");
        var origin = Wire.Decode<Origin>(ProtectedDraftPayload.Read(sourceFile));
        if (origin.SchemaVersion != 1 || origin.Application != c.ApplicationId || origin.Station != c.StationId || origin.Mode != c.Mode || origin.PortableSynthetic || !Guid.TryParseExact(origin.Id, "N", out _))
            throw new InvalidDataException("原模拟机器身份不符；不能补盖新身份。");
        target = Path.GetFullPath(target);
        if (!string.Equals(WindowsStoragePaths.CanonicalDirectory(origin.Directory), target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原逻辑目录未指向本次同一物理目标；不能将任意复制目录当作原机。");
        var current = Wire.Decode<Origin>(ProtectedDraftPayload.Read(targetFile));
        if (current == (origin with { Directory = target })) return origin.Id; // Resume after the locator was durably written.
        if (Wire.FileHash(targetFile) != expectedOriginHash) throw new InvalidDataException("目标机器来源不是原密文，保留原件核对。");
        // Keep the SAME opaque machine identity. This is an approved empty simulation
        // directory relocation, never EnsureNew on a pre-existing ledger or a fresh claim.
        ProtectedDraftPayload.Write(targetFile, Wire.Encode(origin with { Directory = target }), false, runtimeSid);
        Require(target, c); return origin.Id;
    }
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
