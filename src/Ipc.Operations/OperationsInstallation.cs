using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Operations;

// No arbitrary data-root, executable or trust-file override is exposed to the tool.
public sealed class OperationsInstallation
{
    public DeploymentLocation Location { get; }
    public string Identity { get; }
    public string PipeName => "ImporterPc.Operations.v1." + Identity[..32];
    readonly string executable;
    readonly Action verifyCurrent;
    internal Func<IDisposable> AcquireProgramUse { get; }

    internal OperationsInstallation(DeploymentLocation location, string executable, Action verifyCurrent, Func<IDisposable> acquireProgramUse)
    {
        Location = location; this.executable = Path.GetFullPath(executable);
        Identity = Wire.Hash(Wire.Encode(location)); this.verifyCurrent = verifyCurrent; AcquireProgramUse = acquireProgramUse;
    }

    public static OperationsInstallation Load(string directory) => Load(directory, expectedApplicationId: null);

    public static OperationsInstallation Load(string directory, string? expectedApplicationId)
    {
        if (expectedApplicationId is not null && ProductDeploymentCatalog.Find(expectedApplicationId)?.SupportsStandaloneOperationsTool != true)
            throw new UnauthorizedAccessException("此产品未声明独立运维入口。");
        var requestedDesktop = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = ProductDeploymentCatalog.OperationsEnabledProducts.Select(WindowsDeploymentSetup.Current)
            .SingleOrDefault(location => location is not null && requestedDesktop.Equals(
                Path.Combine(location.ReleaseRoot, "desktop"), StringComparison.OrdinalIgnoreCase));
        if (candidate is null || expectedApplicationId is not null && candidate.ApplicationId != expectedApplicationId)
            throw new UnauthorizedAccessException("未找到当前受保护安装登记，请从已安装的供包运维工具打开。");
        var applicationId = candidate.ApplicationId;
        var current = DeploymentMedia.ReadInstalled(directory, applicationId, requireReady: false)
            ?? throw new UnauthorizedAccessException("未找到当前受保护安装登记，请从已安装的供包运维工具打开。");
        var desktop = Path.Combine(current.ReleaseRoot, "desktop");
        if (!requestedDesktop.Equals(desktop, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("运维入口不属于当前安装。");
        var installation = new OperationsInstallation(current, Path.Combine(desktop, InstallationIdentity.DesktopExecutable),
            () =>
            {
                var registered = WindowsDeploymentSetup.Current(applicationId);
                if (registered is null || registered.LifecycleState != "Installed" || Wire.Encode(registered) != Wire.Encode(current))
                    throw new UnauthorizedAccessException("安装登记已变化，请关闭并从当前安装重新打开。");
            }, () => DeploymentProgramUse.Observe(current.ReleaseRoot));
        installation.RequireRead();
        // Pin the release manifest to the protected inventory, then verify the managed
        // code/apphosts the two hosts can load. Vendor SDKs are never loaded by this tool.
        var inventory = current.InventoryFile ?? throw new InvalidDataException("安装库存缺失。");
        MachineRuntimePolicy.RequireProtectedEvidence(inventory);
        if (new FileInfo(inventory).Length > 4 * 1024 * 1024 || Wire.FileHash(inventory) != current.InventorySha256)
            throw new InvalidDataException("安装库存身份不符。");
        using var document = JsonDocument.Parse(File.ReadAllText(inventory));
        if (document.RootElement.GetProperty("ApplicationId").GetString() != applicationId ||
            document.RootElement.GetProperty("SourceCommit").GetString() != current.SourceCommit ||
            document.RootElement.GetProperty("ReleaseRoot").GetString() != current.ReleaseRoot)
            throw new InvalidDataException("安装库存归属不符。");
        var files = document.RootElement.GetProperty("Files");
        foreach (var name in new[] { InstallationIdentity.DesktopExecutable, "Ipc.Operations.Desktop.dll", "application-release.json" }
            .Concat(Directory.EnumerateFiles(desktop, "*.dll").Select(Path.GetFileName)!)
            .Concat(Directory.EnumerateFiles(desktop, "*.json").Select(Path.GetFileName)!))
        {
            var file = Path.Combine(desktop, name!); MachineRuntimePolicy.RequireProtectedEvidence(file);
            if (!files.TryGetProperty("desktop/" + name, out var hash) || Wire.FileHash(file) != hash.GetString())
                throw new InvalidDataException("运维程序与当前安装清单不一致。");
        }
        return installation;
    }

    public CancellationToken RequireRead()
    {
        verifyCurrent();
        using var identity = WindowsIdentity.GetCurrent();
        if (!MachineRuntimePolicy.MatchesRuntimeAccount(identity.User?.Value, Location.RuntimeAccountSid, identity.Claims) || identity.IsSystem)
            throw new UnauthorizedAccessException("当前 Windows 账户没有本站只读运维权限，请使用安装指定的日常账户。");
        return CancellationToken.None;
    }

    internal void RequirePeer(Process process, bool server)
    {
        RequireRead();
        var path = process.MainModule?.FileName;
        if (process.HasExited || process.SessionId != Process.GetCurrentProcess().SessionId ||
            !string.Equals(path, executable, StringComparison.OrdinalIgnoreCase) ||
            !OperationsProcessRole.Matches(process, server))
            throw new UnauthorizedAccessException("连接进程不属于当前安装或当前 Windows 会话。");
    }

    public string[] LogDirectories()
    {
        RequireRead();
        var root = Location.DataRoot; DeploymentPaths.RequireOrdinaryAncestors(root);
        if (!Directory.Exists(root)) return [];
        var result = new List<string>(); int count = 0;
        foreach (var mode in Enum.GetNames<RunMode>())
        {
            var area = Path.Combine(root, mode); DeploymentPaths.RequireOrdinaryAncestors(area);
            if (!Directory.Exists(area)) continue;
            foreach (var station in Directory.EnumerateDirectories(area))
            {
                if (++count > 1024) throw new InvalidDataException("本站目录超过查看预算，请检查安装数据目录。");
                var path = Path.Combine(station, Location.ApplicationId, "diagnostics"); DeploymentPaths.RequireOrdinaryAncestors(path);
                if (Directory.Exists(path)) result.Add(path);
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
