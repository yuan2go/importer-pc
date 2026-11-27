using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;

namespace Ipc.Presentation.Wpf;

public sealed class UiText : INotifyPropertyChanged
{
    const string DefaultLanguage = "zh-CN";
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> dictionaries = new(StringComparer.Ordinal);
    static volatile string language = DefaultLanguage;
    static readonly Lazy<IReadOnlyDictionary<string, string>> authoredKeys = new(() => Strings(DefaultLanguage)
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal));

    public static UiText Current { get; } = new();
    public static string Language => language;
    public string CurrentLanguage => Language;
    UiText() { }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public static string Get(string key)
    {
        var selected = language;
        if (Strings(selected).TryGetValue(key, out var value)) return value;
        return Strings(DefaultLanguage).GetValueOrDefault(key) ?? throw new KeyNotFoundException("Missing UI string resource: " + key);
    }

    // The caller must provide an authored UI label, never an operational value or remote message.
    public static string Authored(string label) => AuthoredKey(label) is { } key ? Get(key) : label;
    internal static string? AuthoredKey(string label) => authoredKeys.Value.GetValueOrDefault(label);

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo(language), Get(key), args);

    internal static IReadOnlyDictionary<string, string> Strings(string culture) => dictionaries.GetOrAdd(culture, static code =>
    {
        using var input = typeof(UiText).Assembly.GetManifestResourceStream("Ipc.UiStrings." + code + ".xaml")
            ?? throw new InvalidOperationException("Built-in UI string dictionary unavailable: " + code);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(input).Root!.Elements().ToDictionary(
            entry => (string?)entry.Attribute(xaml + "Key") ?? throw new InvalidOperationException("Missing UI string key"),
            entry => entry.Value, StringComparer.Ordinal);
    });

    internal static void SetLanguage(string code)
    {
        if (language == code) return;
        _ = Strings(code);
        language = code;
        Current.PropertyChanged?.Invoke(Current, new("Item[]"));
        Current.PropertyChanged?.Invoke(Current, new(nameof(CurrentLanguage)));
    }
}
