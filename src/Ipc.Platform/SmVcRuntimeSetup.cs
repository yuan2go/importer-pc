using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ipc.Platform;

[SupportedOSPlatform("windows")]
internal static class SmVcRuntimeSetup
{
    const string DirectoryName = "prerequisites/";
    const string ActivationFile = DirectoryName + "vc90-probe.manifest";
    const string Vc2008 = DirectoryName + "vc2008-sp1-x64.exe";
    const string Vc2010 = DirectoryName + "vc2010-sp1-x64.exe";
    const uint SearchSystem32 = 0x800;
    sealed record Package(string File, long Length, string Sha256, string Url);
    sealed record Inventory(string ActivationManifest, Package[] Packages);
    internal sealed record Outcome(string Package, string Status, int? ExitCode = null);

    internal static Outcome[] Ensure(DeploymentMedia media, string release, Action<WindowsInstallationStage>? progress)
    {
        if (media.ApplicationId != "sportmaster" || !media.Files.ContainsKey("desktop/vendor/hikrobot-mvid/MVIDCodeReader.dll") &&
            !media.Files.ContainsKey(ActivationFile) && !media.Files.ContainsKey(Vc2008) && !media.Files.ContainsKey(Vc2010)) return [];
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("海康原生识码组件需要 64 位 Windows 安装环境。");

        using var resource = typeof(SmVcRuntimeSetup).Assembly.GetManifestResourceStream("Ipc.SmVcRuntime.json")!;
        using var reader = new StreamReader(resource);
        var inventory = Wire.Decode<Inventory>(reader.ReadToEnd());
        var manifest = Verify(ActivationFile, Wire.Hash(inventory.ActivationManifest));
        var packages = inventory.Packages.ToDictionary(x => DirectoryName + x.File);
        var old = Verify(Vc2008, packages[Vc2008].Sha256, packages[Vc2008].Length);
        var next = Verify(Vc2010, packages[Vc2010].Sha256, packages[Vc2010].Length);
        using var manifestHold = OpenPinned(manifest, Wire.Hash(inventory.ActivationManifest));
        using var oldHold = OpenPinned(old, packages[Vc2008].Sha256);
        using var nextHold = OpenPinned(next, packages[Vc2010].Sha256);

        progress?.Invoke(WindowsInstallationStage.InstallingCameraRuntime);
        return
        [
            EnsureOne("Visual C++ 2008 SP1 x64", old, () => CanLoadVc90(manifest)),
            EnsureOne("Visual C++ 2010 SP1 x64", next, () =>
                CanLoad(Path.Combine(Environment.SystemDirectory, "msvcr100.dll")))
        ];

        string Verify(string relative, string hash, long? bytes = null)
        {
            var path = Path.Combine(release, relative);
            if (!media.Files.TryGetValue(relative, out var listed) || !string.Equals(listed, hash, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || bytes is { } length && new FileInfo(path).Length != length)
                throw new InvalidDataException("海康原生识码运行库安装文件缺失或校验不符，请重新取得完整安装包。");
            MachineRuntimePolicy.RequireProtectedEvidence(path);
            return path;
        }
    }

    static FileStream OpenPinned(string path, string hash)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!string.Equals(Wire.FileHash(path), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("相机运行库安装文件校验失败。");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    static Outcome EnsureOne(string name, string installer, Func<bool> available)
    {
        if (available()) return new(name, "AlreadyAvailable");
        using var child = Process.Start(new ProcessStartInfo(installer, "/q /norestart")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(installer)!
        }) ?? throw new IOException("无法启动相机运行库安装：" + name);
        // The elevated installation already owns the deployment transaction.
        // Do not report success or release it while the vendor installer runs.
        child.WaitForExit();
        var result = child.ExitCode;
        if (result is 0 or 1638 or 3010 && available()) return new(name, "Installed", result);
        var guidance = result switch
        {
            1618 => "Windows 正在执行另一项安装，请等待完成后重试。",
            1641 or 3010 => "请重启 Windows 后重新运行完整安装包。",
            _ => "请查看 Windows 安装日志，处理后重新运行完整安装包。"
        };
        throw new IOException($"相机运行库 {name} 未能就绪。{guidance}（安装退出码 {result}）");
    }

    // Check the same amd64 side-by-side assembly identity requested by MVID.
    // An x86 package or an unrelated VC runtime cannot satisfy this activation.
    static bool CanLoadVc90(string manifest)
    {
        var context = new ActivationContext { Size = (uint)Marshal.SizeOf<ActivationContext>(), Source = manifest };
        var handle = CreateActCtx(ref context);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 14001 or 14003 or 14081) return false;
            throw new Win32Exception(error, "无法检查 Visual C++ 2008 x64 运行库。");
        }
        try
        {
            if (!ActivateActCtx(handle, out var cookie)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return CanLoad("msvcr90.dll") && CanLoad("msvcp90.dll"); }
            finally { DeactivateActCtx(0, cookie); }
        }
        finally { ReleaseActCtx(handle); }
    }

    static bool CanLoad(string name)
    {
        var handle = LoadLibraryEx(name, IntPtr.Zero, SearchSystem32);
        if (handle == IntPtr.Zero) return false;
        FreeLibrary(handle);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ActivationContext
    {
        public uint Size, Flags;
        public string Source;
        public ushort ProcessorArchitecture, LanguageId;
        public IntPtr AssemblyDirectory, ResourceName, ApplicationName, Module;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateActCtxW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateActCtx(ref ActivationContext context);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ActivateActCtx(IntPtr context, out nuint cookie);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeactivateActCtx(uint flags, nuint cookie);
    [DllImport("kernel32.dll")]
    static extern void ReleaseActCtx(IntPtr context);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryEx(string name, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr module);
}
