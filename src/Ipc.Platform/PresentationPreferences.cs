using System.Globalization;
using System.Security.AccessControl;

namespace Ipc.Platform;

// Per-user display choices; never a deployment/configuration/business authority.
public static class PresentationPreferences
{
    // Isolated native verification only; no command line/configuration exposes this.
    internal static Func<string>? UserRootForVerification = null;
    public enum WindowOpeningMode { Window, FullScreen }
    public const int DefaultCueVolume = 80;
    public const string DefaultLanguage = "zh-CN";
    public static IReadOnlyList<string> SupportedLanguages { get; } =
        Array.AsReadOnly(new[] { DefaultLanguage, "en-US", "ru-RU" });
    sealed record AudioPreference(int Schema, int Volume);
    sealed record ThemePreference(int Schema, string ThemeId);
    sealed record LanguagePreference(int Schema, string Language);
    public static event Action<string, string>? LanguageChanged;
    public static event Action<string, string>? ThemeChanged;
    public static event Action<string, int>? CueVolumeChanged;
    public static event Action<string, WindowOpeningMode>? WindowModeChanged;
    public static event Action<string, string, bool>? DisplayChanged;
    // Reconciliation can prove that an earlier atomic write committed even when
    // its notification was lost. Reapply that exact saved preference without
    // rewriting the file or touching another preference's failed draft.
    public static void RefreshSaved(string application, string surface)
    {
        switch (surface)
        {
            case "window": WindowModeChanged?.Invoke(application, ReadWindowMode(application)); break;
            case "audio": CueVolumeChanged?.Invoke(application, ReadCueVolume(application)); break;
            case "language":
                if (ReadSavedLanguage(application) is { } language) LanguageChanged?.Invoke(application, language);
                break;
            case "theme":
                if (ReadSavedTheme(application) is { } theme) ThemeChanged?.Invoke(application, theme);
                break;
            case "pace": DisplayChanged?.Invoke(application, surface, Read(application, surface) ?? true); break;
            default: throw new ArgumentException("Unsupported saved preference surface", nameof(surface));
        }
    }
    static T? ReadPreference<T>(string application, string surface, string oversized, string? damaged = null) where T : class
    {
        var path = FileFor(application, surface);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 4096) throw new InvalidDataException(oversized);
            using var reader = new StreamReader(file);
            try { return Wire.Decode<T>(reader.ReadToEnd()); }
            catch (Exception error) when (damaged is not null && error is (System.Text.Json.JsonException or InvalidDataException))
            { throw new InvalidDataException(damaged, error); }
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    static bool ValidThemeId(string? id) => id is { Length: > 0 and <= 64 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    static string? ReadSavedTheme(string application)
    {
        var value = ReadPreference<ThemePreference>(application, "theme", "界面主题偏好超出预算；保留原文件", "界面主题偏好已损坏；保留原文件");
        if (value is null) return null;
        if (value.Schema != 1 || !ValidThemeId(value.ThemeId)) throw new InvalidDataException("界面主题偏好版本或标识无效；保留原文件");
        return value.ThemeId;
    }

    public static string ReadTheme(string application, string defaultThemeId) => ReadSavedTheme(application) ?? defaultThemeId;

    public static void SaveTheme(string application, string themeId)
    {
        if (!ValidThemeId(themeId)) throw new InvalidDataException("界面主题标识无效");
        _ = ReadSavedTheme(application);
        var file = FileFor(application, "theme");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ExtensionStore.AtomicWrite(file, new ThemePreference(1, themeId));
        ThemeChanged?.Invoke(application, themeId);
    }

    public static string LanguagePreferenceFile(string application) => FileFor(application, "language");

    static string? ReadSavedLanguage(string application)
    {
        var value = ReadPreference<LanguagePreference>(application, "language", "界面语言偏好超出预算；保留原文件",
            "界面语言偏好已损坏；保留原文件");
        if (value is null) return null;
        if (value.Schema != 1 || !SupportedLanguages.Contains(value.Language, StringComparer.Ordinal))
            throw new InvalidDataException("界面语言偏好版本或语言无效；保留原文件");
        return value.Language;
    }

    public static string ReadLanguage(string application, string defaultLanguage = DefaultLanguage) =>
        ReadSavedLanguage(application) ?? (SupportedLanguages.Contains(defaultLanguage, StringComparer.Ordinal) ? defaultLanguage : DefaultLanguage);

    public static void SaveLanguage(string application, string language)
    {
        if (!SupportedLanguages.Contains(language, StringComparer.Ordinal)) throw new InvalidDataException("界面语言无效");
        _ = ReadSavedLanguage(application);
        var file = FileFor(application, "language");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ExtensionStore.AtomicWrite(file, new LanguagePreference(1, language));
        LanguageChanged?.Invoke(application, language);
    }

    public static string ResolveInitialLanguage(string? preferred, string? osCultureName)
    {
        if (preferred is not null && SupportedLanguages.Contains(preferred, StringComparer.Ordinal)) return preferred;
        var culture = osCultureName ?? CultureInfo.CurrentUICulture.Name;
        var language = culture.Split('-', 2)[0];
        if (language.Equals("ru", StringComparison.OrdinalIgnoreCase)) return "ru-RU";
        return language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : DefaultLanguage;
    }

    public static int ReadCueVolume(string application)
    {
        var value = ReadPreference<AudioPreference>(application, "audio", "提示音偏好超出预算；保留原文件");
        if (value is null) return DefaultCueVolume;
        if (value.Schema != 1 || value.Volume is < 0 or > 100) throw new InvalidDataException("提示音音量无效；保留原文件");
        return value.Volume;
    }
    public static void SaveCueVolume(string application, int volume)
    {
        if (volume is < 0 or > 100) throw new InvalidDataException("提示音音量须为0～100");
        _ = ReadCueVolume(application);
        var file = FileFor(application, "audio");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ExtensionStore.AtomicWrite(file, new AudioPreference(1, volume));
        CueVolumeChanged?.Invoke(application, volume);
    }
    sealed record WindowPreference(int Schema, WindowOpeningMode Mode);
    public static WindowOpeningMode ReadWindowMode(string application)
    {
        var value = ReadPreference<WindowPreference>(application, "window", "窗口偏好超出预算；保留原文件");
        if (value is null) return WindowOpeningMode.Window;
        if (value.Schema != 1 || !Enum.IsDefined(value.Mode)) throw new InvalidDataException("窗口偏好版本或模式不支持；保留原文件");
        return value.Mode;
    }
    public static void SaveWindowMode(string application, WindowOpeningMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("请选择窗口或最大化（全屏）");
        _ = ReadWindowMode(application);
        var file = FileFor(application, "window");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, new WindowPreference(1, mode));
        WindowModeChanged?.Invoke(application, mode);
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
        var preference = ReadPreference<Preference>(application, surface, "展示偏好超出预算");
        if (preference is null) return null;
        if (preference.Schema != 1) throw new InvalidDataException("展示偏好版本不支持；保留原文件");
        return preference.Compact;
    }
    public static void Save(string application, string surface, bool compact)
    {
        if (surface is "audio" or "window" or "theme" or "language" || surface.StartsWith("station-", StringComparison.Ordinal)) throw new InvalidDataException("请使用对应的专用偏好保存入口。");
        var file = FileFor(application, surface); _ = Read(application, surface);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, new Preference(1, compact)); DisplayChanged?.Invoke(application, surface, compact);
    }
    static string StationSurface(string station) => "station-" + Wire.Hash(station)[..24].ToLowerInvariant();
    public static StationDisplay ReadStation(string application, string station)
    {
        var value = ReadPreference<StationDisplay>(application, StationSurface(station), "站点显示偏好超出预算；保留原文件");
        if (value is null) return new(1, "", null);
        ValidateStation(value); return value;
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
