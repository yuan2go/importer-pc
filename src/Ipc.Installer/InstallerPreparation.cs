using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Ipc.Desktop;

namespace Ipc.Installer;

// Preparation feeds the existing setup window; no second UI or elevated client.
sealed class InstallerPreparation
{
    string? workspace;
    static Stream Resource(string name) => System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidDataException("内嵌安装文件缺失，请使用完整发布的安装程序");
    readonly PayloadDescriptor descriptor;
    public string BusinessVersion => descriptor.BusinessVersion;
    public InstallerPreparation()
    {
        using var metadata = Resource("Ipc.Installer.payload.json");
        descriptor = JsonSerializer.Deserialize<PayloadDescriptor>(metadata) ?? throw new InvalidDataException("安装包说明缺失");
    }
    public async Task<string> PrepareAsync(IProgress<InstallationPreparationProgress> progress, CancellationToken cancellation)
    {
        Cleanup();
        var root = Path.GetFullPath(Path.GetTempPath()); InstallerPayload.RequireOrdinaryAncestors(root);
        var path = Path.Combine(root, "ipc-installer-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("安装准备目录已被占用");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) }.DistinctBy(sid => sid.Value))
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security); workspace = path;
        using var input = Resource("Ipc.Installer.payload.zip");
        await Task.Run(() => InstallerPayload.PrepareAsync(input, descriptor, Path.Combine(workspace, "media"), new PreparationProgressAdapter(progress), cancellation), cancellation);
        return Path.Combine(workspace, "media");
    }
    sealed class PreparationProgressAdapter(IProgress<InstallationPreparationProgress> progress) : IProgress<PreparationProgress>
    { public void Report(PreparationProgress value) => progress.Report(new(value.Stage, value.File, value.Percent)); }
    public void Cleanup()
    {
        if (workspace is null) return;
        var full = Path.GetFullPath(workspace); var temporary = Path.GetFullPath(Path.GetTempPath());
        if (Path.GetDirectoryName(full) != temporary.TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("ipc-installer-", StringComparison.Ordinal))
            throw new IOException("拒绝清理不属于本次安装的目录");
        InstallerPayload.RequireOrdinaryAncestors(full);
        if (Directory.Exists(full))
        {
            var pending = new Stack<string>(); pending.Push(full);
            while (pending.TryPop(out var directory))
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("准备目录出现链接，保留目录：" + full);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            Directory.Delete(full, true);
        }
        workspace = null;
    }
}
