using System.Security.AccessControl;

namespace Ipc.Platform;

// Per-user display choices; never a deployment/configuration/business authority.
public static class PresentationPreferences
{
    // Isolated native verification only; no command line/configuration exposes this.
    internal static Func<string>? UserRootForVerification = null;
    public enum WindowOpeningMode { Window, FullScreen }
    sealed record WindowPreference(int Schema, WindowOpeningMode Mode);
    public static WindowOpeningMode ReadWindowMode(string application)
    {
        var file = FileFor(application, "window");
        if (!File.Exists(file)) return WindowOpeningMode.Window;
        if (new FileInfo(file).Length > 4096) throw new InvalidDataException("窗口偏好超出预算；保留原文件");
        var value = Wire.Decode<WindowPreference>(File.ReadAllText(file));
        if (value.Schema != 1 || !Enum.IsDefined(value.Mode)) throw new InvalidDataException("窗口偏好版本或模式不支持；保留原文件");
        return value.Mode;
    }
    public static void SaveWindowMode(string application, WindowOpeningMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("请选择窗口或最大化（全屏）");
        _ = ReadWindowMode(application);
        var file = FileFor(application, "window");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, new WindowPreference(1, mode));
    }
    public sealed record StationDisplay(int Schema, string Name, int? TargetPerHour);
    sealed record Preference(int Schema, bool Compact);
    static string FileFor(string application, string surface)
    {
        if (!ExtensionSchema.IsId(application) || !ExtensionSchema.IsId(surface)) throw new InvalidDataException("Invalid presentation identity");
        var root = UserRootForVerification?.Invoke() ?? WindowsStoragePaths.UserRoot;
        if (UserRootForVerification is null && OperatingSystem.IsWindows() && Directory.Exists(root))
        {
            using var caller = System.Security.Principal.WindowsIdentity.GetCurrent();
            var owner = (System.Security.Principal.SecurityIdentifier)new DirectoryInfo(root).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner).GetOwner(typeof(System.Security.Principal.SecurityIdentifier))!;
            if (owner.Value != caller.User?.Value && owner.Value is not ("S-1-5-18" or "S-1-5-32-544"))
                throw new UnauthorizedAccessException("个人偏好目录不属于当前Windows用户；保留原目录，请使用安装修复核对。");
        }
        var path = Path.Combine(root, "presentation", application + "." + surface + ".json");
        DeploymentPaths.RequireOrdinaryAncestors(path); return path;
    }
    public static bool? Read(string application, string surface)
    {
        var file = FileFor(application, surface); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 4096) throw new InvalidDataException("展示偏好超出预算");
        var preference = Wire.Decode<Preference>(File.ReadAllText(file));
        if (preference.Schema != 1) throw new InvalidDataException("展示偏好版本不支持；保留原文件");
        return preference.Compact;
    }
    public static void Save(string application, string surface, bool compact)
    {
        var file = FileFor(application, surface); _ = Read(application, surface);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, new Preference(1, compact));
    }
    static string StationSurface(string station) => "station-" + Wire.Hash(station)[..24].ToLowerInvariant();
    public static StationDisplay ReadStation(string application, string station)
    {
        var file = FileFor(application, StationSurface(station));
        if (!File.Exists(file)) return new(1, "", null);
        if (new FileInfo(file).Length > 4096) throw new InvalidDataException("站点显示偏好超出预算；保留原文件");
        var value = Wire.Decode<StationDisplay>(File.ReadAllText(file)); ValidateStation(value); return value;
    }
    public static void ValidateStation(StationDisplay value)
    {
        if (value.Schema != 1) throw new InvalidDataException("站点显示偏好版本不支持；保留原文件");
        if (value.Name is null || value.Name.Length > 64 || value.Name.Any(char.IsControl)) throw new InvalidDataException("站点显示名称最多64字且不含控制字符");
        if (value.TargetPerHour is < 1 or > 100000) throw new InvalidDataException("目标效率须为1～100000件/小时；留空不显示");
    }
    public static void SaveStation(string application, string station, StationDisplay value)
    {
        ValidateStation(value); _ = ReadStation(application, station);
        var file = FileFor(application, StationSurface(station));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, value);
    }
}
