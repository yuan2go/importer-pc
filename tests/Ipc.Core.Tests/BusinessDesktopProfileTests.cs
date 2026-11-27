using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass]
public sealed class BusinessDesktopProfileTests
{
    [TestMethod]
    [DataRow("sportmaster", true, true)]
    [DataRow("direct-plc", false, true)]
    [DataRow("wcs-to-plc", false, true)]
    [DataRow("unknown-app", false, false)]
    public void DesktopCatalogPreservesBusinessBoundaries(string id, bool station, bool registered)
    {
        var profile = BusinessDesktopCatalog.For(id);
        Assert.AreEqual(registered ? id : "", profile.ApplicationId);
        Assert.AreEqual(registered, profile.SupportsThemePreference);
        Assert.AreEqual(station ? "supply-station" : "classic", profile.DefaultDesktopUi);
        Assert.AreEqual(station ? "SM " : "", profile.InstallerVersionPrefix);
        Assert.AreEqual(station, profile.UsesStationDisplayPreferences);
        Assert.AreEqual(station, profile.SupportsFirstConfigurationBarcodeTest);
        Assert.AreEqual(station, profile.UsesWorkstationSettingsFlow);
        Assert.AreEqual(station, profile.UsesFirstReadinessStep);
        Assert.AreEqual(station, profile.SupportsBackupSetsDialog);
        Assert.AreEqual(station, profile.UsesFullScreenOpeningPreference);
        Assert.AreEqual(station, profile.UsesScanAndBarcodePolicy);
        Assert.AreEqual(!station, profile.UsesLegacyWeightAndQueueFields);
        Assert.AreEqual(station, profile.HidesUnboundSecondaryCapabilities);
        Assert.AreEqual(station, profile.UsesCompactHistoryLayout);
        Assert.AreEqual(station, profile.UsesSmCapacityProjection);
        Assert.AreEqual(station, profile.UsesSmUnknownFaultGuidance);
        Assert.AreEqual(station, profile.ShowsCompactDiskReserveHint);
        CollectionAssert.AreEqual(station ? new[] { 0, 2, 1, 4 } : new[] { 0, 1, 2, 3, 4 }, profile.SetupCategories.ToArray());
        Assert.AreEqual(WorkstationThemeManager.IndustrialLightId, profile.DefaultThemeId);
        CollectionAssert.AreEqual(new[] { WorkstationThemeManager.IndustrialLightId, WorkstationThemeManager.IndustrialDarkId },
            profile.SupportedThemes.Select(theme => theme.Id).ToArray());
    }

    [TestMethod]
    public void DeploymentProfileOwnsNoWorkstationDisplayMetadata()
    {
        foreach (var property in new[] { nameof(BusinessDesktopProfile.DefaultDesktopUi), nameof(BusinessDesktopProfile.UsesStationDisplayPreferences),
                 nameof(BusinessDesktopProfile.SupportsFirstConfigurationBarcodeTest), nameof(BusinessDesktopProfile.InstallerVersionPrefix),
                 nameof(BusinessDesktopProfile.FirstUseGuidance) })
            Assert.IsNull(typeof(ProductDeploymentProfile).GetProperty(property), property);
    }

    [TestMethod]
    public void ReadSaveAndRefreshThemeAreUserAndProductScoped()
    {
        using var user = new IsolatedUser();
        const string app = "direct-plc";
        const string light = WorkstationThemeManager.IndustrialLightId;
        const string dark = WorkstationThemeManager.IndustrialDarkId;
        Assert.AreEqual(light, PresentationPreferences.ReadTheme(app, light));
        var changes = new List<(string, string)>();
        void Changed(string application, string theme) => changes.Add((application, theme));
        PresentationPreferences.ThemeChanged += Changed;
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveTheme(app, "invalid/ID"));
            Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveTheme(app, new string('x', 65)));
            Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveTheme(app, "非ASCII"));
            PresentationPreferences.SaveTheme(app, dark);
            var bytes = File.ReadAllBytes(user.ThemePath(app));
            PresentationPreferences.RefreshSaved(app, "theme");
            Assert.AreEqual(dark, PresentationPreferences.ReadTheme(app, light));
            Assert.AreEqual(light, PresentationPreferences.ReadTheme("wcs-to-plc", light));
            CollectionAssert.AreEqual(new[] { (app, dark), (app, dark) }, changes.ToArray());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(user.ThemePath(app)));

            var draft = new SmDisplayPreferenceDraft(app) { ThemeId = light };
            Assert.IsTrue(draft.HasChanges);
            Assert.AreEqual(nameof(draft.ThemeId), draft.Changes().Single().Path);
            Assert.AreEqual("Saved", draft.Save().Single().State);
            Assert.IsFalse(draft.HasChanges);
            Assert.AreEqual(light, PresentationPreferences.ReadTheme(app, dark));
            Assert.IsFalse(File.Exists(user.ThemePath(app).Replace(".theme.json", ".window.json", StringComparison.Ordinal)));
        }
        finally { PresentationPreferences.ThemeChanged -= Changed; }
    }

    [TestMethod]
    public void ThemeDamageIsReadableAndOriginalIsPreserved()
    {
        using var user = new IsolatedUser();
        const string app = "direct-plc";
        const string dark = WorkstationThemeManager.IndustrialDarkId;
        var path = user.ThemePath(app);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (var content in new[] { "{", "null", "{\"Schema\":2,\"ThemeId\":\"industrial-light\"}", new string('x', 4097) })
        {
            File.WriteAllText(path, content);
            var error = Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.ReadTheme(app, dark));
            StringAssert.Contains(error.Message, "保留原文件");
            Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveTheme(app, dark));
            Assert.AreEqual(content, File.ReadAllText(path));
            var damaged = new SmDisplayPreferenceDraft(app);
            Assert.IsFalse(damaged.CanEdit(nameof(damaged.ThemeId)));
            Assert.AreEqual(WorkstationThemeManager.IndustrialLightId, damaged.ThemeId);
        }
        File.Delete(path);
        PresentationPreferences.SaveTheme(app, "custom-not-installed");
        var draft = new SmDisplayPreferenceDraft(app);
        Assert.AreEqual(WorkstationThemeManager.IndustrialLightId, draft.ThemeId);
        Assert.IsFalse(draft.HasChanges);
        StringAssert.Contains(draft.ThemeStatus, "不可用");
        Assert.AreEqual("custom-not-installed", PresentationPreferences.ReadTheme(app, dark));
    }

    [STATestMethod]
    public void PaletteReplacesResourceSlotAndLiveDependencyProperties()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = application.Resources;
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative)
        });
        var border = new Border();
        border.SetResourceReference(Border.BackgroundProperty, "Background");
        var window = new Window { Content = border, Width = 80, Height = 60, Opacity = 0, ShowInTaskbar = false };
        window.Show();
        try
        {
            Verify();
        }
        finally { window.Close(); }

        void Verify()
        {
            var manager = new WorkstationThemeManager();
            var applied = new List<string>();
            manager.ThemeApplied += applied.Add;
            var count = resources.MergedDictionaries.Count;
            var profile = BusinessDesktopCatalog.For("sportmaster");

            Check("#F1F4F7", "#FFFFFF", "#192736", "#205FCE", "#147450", "#895509", "#B52F38");
            manager.Apply(resources, profile.DefaultThemeId, profile.SupportedThemes, profile.DefaultThemeId);
            var previous = resources.MergedDictionaries[^1];
            manager.Apply(resources, WorkstationThemeManager.IndustrialDark);
            Check("#0F1722", "#172231", "#F1F5F9", "#4C8DFF", "#34D399", "#FBBF24", "#F87171");
            Assert.AreEqual(count + 1, resources.MergedDictionaries.Count);
            Assert.IsFalse(resources.MergedDictionaries.Contains(previous));

            var oem = WorkstationThemeManager.IndustrialDark with { Id = "oem-synthetic" };
            manager.Apply(resources, oem.Id, [.. profile.SupportedThemes, oem], profile.DefaultThemeId);
            Assert.AreEqual(oem.Id, manager.CurrentThemeId);
            var missing = oem with { Id = "missing", PaletteUri = new Uri("/Ipc.Presentation.Wpf;component/Themes/Palettes/Missing.xaml", UriKind.Relative) };
            manager.Apply(resources, missing.Id, [.. profile.SupportedThemes, missing], profile.DefaultThemeId);
            Check("#F1F4F7", "#FFFFFF", "#192736", "#205FCE", "#147450", "#895509", "#B52F38");
            manager.Apply(resources, "unknown-theme", profile.SupportedThemes, profile.DefaultThemeId);
            Assert.AreEqual(count + 1, resources.MergedDictionaries.Count);
            CollectionAssert.AreEqual(new[] { profile.DefaultThemeId, WorkstationThemeManager.IndustrialDarkId, oem.Id, profile.DefaultThemeId, profile.DefaultThemeId }, applied.ToArray());

            void Check(params string[] colors)
            {
                var keys = new[] { "Background", "Surface", "Ink", "Accent", "Success", "Warning", "Danger" };
                for (var index = 0; index < keys.Length; index++)
                    Assert.AreEqual((Color)ColorConverter.ConvertFromString(colors[index]), ((SolidColorBrush)border.FindResource(keys[index])).Color, keys[index]);
                Assert.AreEqual((Color)ColorConverter.ConvertFromString(colors[0]), ((SolidColorBrush)border.Background).Color);
            }
        }
    }

    sealed class IsolatedUser : IDisposable
    {
        static readonly FieldInfo userRoot = typeof(PresentationPreferences).GetField("UserRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!;
        readonly string folder = Path.Combine(Path.GetTempPath(), "ipc-theme-" + Guid.NewGuid().ToString("N"));
        readonly object? previous = userRoot.GetValue(null);

        public IsolatedUser() => userRoot.SetValue(null, (Func<string>)(() => folder));
        public string ThemePath(string app) => Path.Combine(folder, "presentation", app + ".theme.json");
        public void Dispose()
        {
            userRoot.SetValue(null, previous);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
