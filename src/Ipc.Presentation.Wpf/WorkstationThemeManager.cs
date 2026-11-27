using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace Ipc.Presentation.Wpf;

public sealed partial class WorkstationThemeManager
{
    public string CurrentThemeId { get; private set; } = IndustrialLightId;
    public event Action<string>? ThemeApplied;

    public void Apply(ResourceDictionary hostResources, string? themeId, IReadOnlyList<UiThemeDescriptor> available, string defaultThemeId)
    {
        var selected = Resolve(themeId, available, defaultThemeId);
        var fallback = Resolve(null, available, defaultThemeId);
        var palette = TryLoad(selected);
        if (palette is null)
        {
            if (fallback != selected) palette = TryLoad(fallback);
            selected = palette is null ? IndustrialLight : fallback;
        }
        Install(hostResources, selected, palette ?? LoadPalette(IndustrialLight));
    }

    // Call on the host dispatcher. Resolve and check the whole palette before changing the resources in use.
    public void Apply(ResourceDictionary hostResources, UiThemeDescriptor theme) =>
        Install(hostResources, theme, LoadPalette(theme));

    static ResourceDictionary? TryLoad(UiThemeDescriptor theme)
    {
        try { return LoadPalette(theme); }
        catch (Exception error) when (theme != IndustrialLight && error is (IOException or InvalidDataException or XamlParseException or InvalidOperationException))
        { return null; }
    }

    static ResourceDictionary LoadPalette(UiThemeDescriptor theme)
    {
        var palette = new ResourceDictionary { Source = theme.PaletteUri };
        if (theme != IndustrialLight && theme != IndustrialDark)
        {
            var schema = new ResourceDictionary { Source = IndustrialLight.PaletteUri };
            foreach (var key in schema.Keys)
                if (!palette.Contains(key) || palette[key]?.GetType() != schema[key]?.GetType())
                    throw new InvalidDataException("界面主题缺少兼容的语义资源。");
        }
        return palette;
    }

    void Install(ResourceDictionary hostResources, UiThemeDescriptor theme, ResourceDictionary palette)
    {
        var slot = new PaletteSlot();
        slot.MergedDictionaries.Add(palette);
        var dictionaries = hostResources.MergedDictionaries;
        var current = dictionaries.OfType<PaletteSlot>().SingleOrDefault();
        // WPF invalidates DynamicResource references on Add/Remove. A collection
        // Replace can update lookups without refreshing values already in use.
        dictionaries.Add(slot);
        if (current is not null) dictionaries.Remove(current);
        CurrentThemeId = theme.Id;
        ThemeApplied?.Invoke(theme.Id);
    }

    sealed class PaletteSlot : ResourceDictionary { }

}
