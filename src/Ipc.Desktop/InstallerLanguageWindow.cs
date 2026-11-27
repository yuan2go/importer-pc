using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Platform;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Desktop;

public sealed class InstallerLanguageWindow : Window
{
    readonly ComboBox languages;
    public string SelectedLanguage => (string)languages.SelectedValue;

    public InstallerLanguageWindow(string? language = null)
    {
        Title = UiText.Get("Installer.LanguageTitle");
        Width = 660; MinWidth = 480; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        StartupAppearance.Apply(this);
        SetResourceReference(ForegroundProperty, "Ink");

        var panel = new StackPanel { Margin = new(24) };
        var heading = new TextBlock
        {
            Text = UiText.Get("Installer.LanguagePrompt"), FontSize = UiTypography.Group,
            FontWeight = UiTypography.Emphasis, TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(heading);
        languages = new ComboBox
        {
            ItemsSource = WorkstationLocalizationManager.Languages,
            DisplayMemberPath = nameof(UiLanguageDescriptor.NativeName),
            SelectedValuePath = nameof(UiLanguageDescriptor.Code),
            SelectedValue = PresentationPreferences.ResolveInitialLanguage(language, null),
            Margin = new(0, 12, 0, 20), MinHeight = 44
        };
        AutomationProperties.SetAutomationId(languages, "Installer.LanguageSelection");
        AutomationProperties.SetName(languages, heading.Text);
        AutomationProperties.SetLabeledBy(languages, heading);
        panel.Children.Add(languages);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = UiText.Get("Installer.CancelTrilingual"), IsCancel = true, MinHeight = 44, Margin = new(0, 0, 8, 0) };
        var next = new Button { Content = UiText.Get("Installer.ContinueTrilingual"), IsDefault = true, MinHeight = 44 };
        next.SetResourceReference(StyleProperty, "StartupPrimary");
        AutomationProperties.SetAutomationId(cancel, "Installer.LanguageCancel");
        AutomationProperties.SetAutomationId(next, "Installer.LanguageContinue");
        next.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(next);
        panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => languages.Focus();
    }

    public static string? Choose(ResourceDictionary resources)
    {
        var picker = new InstallerLanguageWindow();
        if (picker.ShowDialog() != true) return null;
        new WorkstationLocalizationManager().Apply(resources, picker.SelectedLanguage);
        return picker.SelectedLanguage;
    }
}
