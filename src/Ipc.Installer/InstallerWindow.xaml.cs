using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;

namespace Ipc.Installer;

public partial class InstallerWindow : Window
{
    readonly CancellationTokenSource cancellation = new();
    readonly PayloadDescriptor? descriptor;
    bool busy, handingOff, allowClose;
    string? workspace;

    public InstallerWindow()
    {
        InitializeComponent();
        try
        {
            using var source = Resource("Ipc.Installer.payload.json");
            descriptor = JsonSerializer.Deserialize<PayloadDescriptor>(source) ?? throw new InvalidDataException("安装包说明缺失");
            Version.Text = "版本 " + descriptor.BusinessVersion + " · Windows 64 位";
        }
        catch (Exception error) { ShowError(error); Next.IsEnabled = false; }
        Closing += OnClosing;
    }

    static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidDataException("内嵌安装文件缺失，请使用完整发布的安装程序");

    async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (busy || descriptor is null) return;
        busy = true; Next.IsEnabled = false; Progress.Visibility = Visibility.Visible; Diagnostic.Visibility = Visibility.Collapsed;
        Heading.Text = "正在准备安装";
        Description.Text = "准备完成后将打开安装向导。安装目录和授权将在向导中确认。";
        try
        {
            workspace = CreateWorkspace();
            using var input = Resource("Ipc.Installer.payload.zip");
            var progress = new Progress<PreparationProgress>(value => { Stage.Text = value.Stage; FileName.Text = value.File; FileName.ToolTip = value.File; Progress.Value = value.Percent; });
            var executable = await Task.Run(() => InstallerPayload.PrepareAsync(input, descriptor, Path.Combine(workspace, "media"), progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            using var child = Process.Start(new ProcessStartInfo(executable, "--setup") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(executable))! }) ?? throw new IOException("无法打开安装向导");
            handingOff = true; Hide();
            await child.WaitForExitAsync();
            if (child.ExitCode != 0) throw new IOException("安装向导未正常退出，退出码：" + child.ExitCode);
            // Closing an installer can mean cancellation. Never invent an installation-success fact.
            Cleanup(); allowClose = true; System.Windows.Application.Current.Shutdown(0);
        }
        catch (OperationCanceledException)
        {
            try { Cleanup(); allowClose = true; System.Windows.Application.Current.Shutdown(0); }
            catch (Exception error) { Show(); ShowError(error); }
        }
        catch (Exception error)
        {
            try { Cleanup(); } catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            Show(); ShowError(error); Next.IsEnabled = !cancellation.IsCancellationRequested;
        }
        finally { busy = false; handingOff = false; Cancel.IsEnabled = true; }
    }

    static string CreateWorkspace()
    {
        var root = Path.GetFullPath(Path.GetTempPath()); InstallerPayload.RequireOrdinaryAncestors(root);
        var path = Path.Combine(root, "ipc-installer-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("安装准备目录已被占用");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) }.DistinctBy(sid => sid.Value))
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
        return path;
    }

    void Cleanup()
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

    void ShowError(Exception error)
    {
        Heading.Text = handingOff ? "安装向导已关闭" : "安装准备未完成";
        Stage.Text = handingOff ? "请保留以下诊断。安装结果以安装向导的实际结果为准。" : "可查看以下原因后重试或关闭。";
        Diagnostic.Text = error.ToString(); Diagnostic.Visibility = Visibility.Visible; Progress.Visibility = Visibility.Collapsed;
        Next.Content = "重试";
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (busy)
        {
            e.Cancel = true;
            if (!handingOff) { cancellation.Cancel(); Cancel.IsEnabled = false; Stage.Text = "正在取消并清理准备文件…"; }
            return;
        }
        allowClose = true; System.Windows.Application.Current.Shutdown(0);
    }
}
