using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ipc.Presentation.Wpf;

public sealed record UiLanguageDescriptor(string Code, string NativeName, Uri ResourceUri);

public sealed class WorkstationLocalizationManager
{
    public static IReadOnlyList<UiLanguageDescriptor> Languages { get; } = Array.AsReadOnly(new[]
    {
        Language("zh-CN", "简体中文"), Language("en-US", "English"), Language("ru-RU", "Русский")
    });

    static UiLanguageDescriptor Language(string code, string name) => new(code, name,
        new("/Ipc.Presentation.Wpf;component/Localization/Strings." + code + ".xaml", UriKind.Relative));

    public string CurrentLanguage { get; private set; } = Languages[0].Code;
    public event Action<string>? LanguageApplied;

    public void Apply(ResourceDictionary hostResources, string? languageCode)
    {
        var selected = Languages.FirstOrDefault(value => value.Code == languageCode) ?? Languages[0];
        var strings = new ResourceDictionary { Source = selected.ResourceUri };
        var culture = CultureInfo.GetCultureInfo(selected.Code);
        var slot = new LanguageSlot();
        slot.MergedDictionaries.Add(strings);
        var dictionaries = hostResources.MergedDictionaries;
        var previous = dictionaries.OfType<LanguageSlot>().SingleOrDefault();
        // Adding the complete dictionary before removing the slot refreshes existing DynamicResource expressions.
        dictionaries.Add(slot);
        if (previous is not null) dictionaries.Remove(previous);
        CultureInfo.CurrentUICulture = culture;
        CurrentLanguage = selected.Code;
        UiText.SetLanguage(selected.Code);
        LanguageApplied?.Invoke(selected.Code);
    }

    public static void Bind(DependencyObject element, DependencyProperty property, string key) =>
        BindingOperations.SetBinding(element, property, new Binding("[" + key + "]") { Source = UiText.Current, Mode = BindingMode.OneWay });

    sealed class LanguageSlot : ResourceDictionary { }
}
