using System.ComponentModel;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    partial void InitializeLanguage()
    {
        PropertyChangedEventManager.AddHandler(UiText.Current, LanguageChanged, "Item[]");
        Closed += (_, _) => PropertyChangedEventManager.RemoveHandler(UiText.Current, LanguageChanged, "Item[]");
    }

    void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        ViewModel.RefreshLanguage();
        ApplyNavigation();
    }
}
