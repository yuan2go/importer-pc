namespace Ipc.Presentation.Wpf;

public sealed record UiThemeDescriptor(string Id, string DisplayName, string Description, Uri PaletteUri);

public sealed partial class WorkstationThemeManager
{
    public const string IndustrialLightId = "industrial-light";
    public const string IndustrialDarkId = "industrial-dark";
    public static UiThemeDescriptor IndustrialLight { get; } = BuiltIn(IndustrialLightId, "工业浅色（默认）", "适合常规照明。", "IndustrialLight");
    public static UiThemeDescriptor IndustrialDark { get; } = BuiltIn(IndustrialDarkId, "工业深色（高对比）", "适合低照度作业。", "IndustrialDark");
    public static IReadOnlyList<UiThemeDescriptor> BuiltInThemes { get; } = Array.AsReadOnly(new[] { IndustrialLight, IndustrialDark });

    public static UiThemeDescriptor Resolve(string? themeId, IReadOnlyList<UiThemeDescriptor> available, string defaultThemeId) =>
        available.FirstOrDefault(candidate => candidate.Id == themeId)
        ?? available.FirstOrDefault(candidate => candidate.Id == defaultThemeId)
        ?? IndustrialLight;

    static UiThemeDescriptor BuiltIn(string id, string displayName, string description, string file) =>
        new(id, displayName, description, new Uri("/Ipc.Presentation.Wpf;component/Themes/Palettes/" + file + ".xaml", UriKind.Relative));
}
