using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Presentation.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class MultiBusinessCompositionTests : CoreRegression
{
    [TestMethod]
    [TestCategory("Core")]
    public Task ComposedCameraScaleLoginAndPlc() => RunSuite("--multi-business-composition");

    [STATestMethod]
    public void ComposedDesktopProfileLoadsItsPaletteAndSwitchesLanguage()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var theme = new UiThemeDescriptor("weigh-plc-night", "Weigh PLC night", "Demo station palette",
            new Uri("/Ipc.Core.Tests;component/WeighPlcTestPalette.xaml", UriKind.Relative));
        var profile = new BusinessDesktopProfile("weigh-plc-demo", "Weigh PLC")
        {
            DefaultThemeId = theme.Id,
            SupportedThemes = [WorkstationThemeManager.IndustrialLight, theme]
        };
        var view = new StackPanel();
        var status = new TextBlock();
        status.SetResourceReference(TextBlock.TextProperty, "Work.WaitingToLoad");
        view.Children.Add(status);
        var palette = new WorkstationThemeManager();
        palette.Apply(view.Resources, null, profile.SupportedThemes, profile.DefaultThemeId);
        Assert.AreEqual(theme.Id, palette.CurrentThemeId);
        Assert.AreEqual(Color.FromRgb(128, 80, 194), ((SolidColorBrush)view.FindResource("Accent")).Color);

        var culture = CultureInfo.CurrentUICulture;
        var language = UiText.Language;
        var localization = new WorkstationLocalizationManager();
        try
        {
            foreach (var (code, expected) in new[]
            {
                ("zh-CN", "等待投包"),
                ("en-US", "Waiting for a parcel"),
                ("ru-RU", "Ожидается посылка")
            })
            {
                localization.Apply(view.Resources, code);
                Assert.AreEqual(expected, status.Text);
                Assert.AreEqual(theme.Id, palette.CurrentThemeId);
            }
        }
        finally
        {
            localization.Apply(view.Resources, language);
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
