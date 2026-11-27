using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass]
public sealed class LocalizationTests
{
    [STATestMethod]
    public void DictionariesAgreeOnKeysAndFormatArguments()
    {
        _ = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dictionaries = WorkstationLocalizationManager.Languages.Select(language =>
        {
            var dictionary = new ResourceDictionary { Source = language.ResourceUri };
            return dictionary.Keys.Cast<string>().ToDictionary(key => key, key => (string)dictionary[key], StringComparer.Ordinal);
        }).ToArray();

        Assert.IsTrue(dictionaries[0].Count > 0);
        foreach (var dictionary in dictionaries)
        {
            CollectionAssert.AreEquivalent(dictionaries[0].Keys.ToArray(), dictionary.Keys.ToArray());
            foreach (var (key, value) in dictionary)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), key);
                var indices = Arguments(value);
                CollectionAssert.AreEqual(Arguments(dictionaries[0][key]), indices, key);
                Assert.AreEqual(indices.Length == 0 ? 0 : indices[^1] + 1, CompositeFormat.Parse(value).MinimumArgumentCount, key);
            }
        }

        static int[] Arguments(string value) => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,:}])")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();
    }

    [STATestMethod]
    public void SwitchLanguageUpdatesLiveControlsWithoutLeakingSlots()
    {
        var parent = new StackPanel();
        var caption = new TextBlock();
        var codeCaption = new TextBlock();
        caption.SetResourceReference(TextBlock.TextProperty, "Work.WaitingToLoad");
        WorkstationLocalizationManager.Bind(codeCaption, TextBlock.TextProperty, "Work.WaitingToLoad");
        parent.Children.Add(caption);
        parent.Children.Add(codeCaption);
        var baseline = new ResourceDictionary { Source = WorkstationLocalizationManager.Languages[0].ResourceUri };
        parent.Resources.MergedDictionaries.Add(baseline);
        var manager = new WorkstationLocalizationManager();
        var applied = new List<string>();
        var originalCulture = CultureInfo.CurrentUICulture;
        var originalLanguage = UiText.Language;
        manager.LanguageApplied += applied.Add;
        try
        {
            foreach (var (locale, expectedCode, title) in new[]
            {
                ("zh-CN", "zh-CN", "等待投包"),
                ("en-US", "en-US", "Waiting for a parcel"),
                ("ru-RU", "ru-RU", "Ожидается посылка"),
                ("zh-CN", "zh-CN", "等待投包"),
                ("unavailable", "zh-CN", "等待投包")
            })
            {
                var previous = parent.Resources.MergedDictionaries.Skip(1).SingleOrDefault();
                manager.Apply(parent.Resources, locale);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(expectedCode, manager.CurrentLanguage);
                Assert.AreEqual(expectedCode, CultureInfo.CurrentUICulture.Name);
                Assert.AreEqual(title, caption.Text);
                Assert.AreEqual(title, codeCaption.Text);
                Assert.AreEqual(2, parent.Resources.MergedDictionaries.Count);
                Assert.AreSame(baseline, parent.Resources.MergedDictionaries[0]);
                if (previous is not null) Assert.IsFalse(parent.Resources.MergedDictionaries.Contains(previous));
            }
            CollectionAssert.AreEqual(new[] { "zh-CN", "en-US", "ru-RU", "zh-CN", "zh-CN" }, applied);
        }
        finally
        {
            manager.LanguageApplied -= applied.Add;
            manager.Apply(parent.Resources, originalLanguage);
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [TestMethod]
    [DataRow(null, "ru-UA", "ru-RU")]
    [DataRow(null, "en-GB", "en-US")]
    [DataRow(null, "zh-TW", "zh-CN")]
    [DataRow("it-IT", "de-DE", "zh-CN")]
    [DataRow("ru-RU", "en-GB", "ru-RU")]
    public void SetupPrefersExplicitSupportedLanguageThenOs(string? explicitCode, string culture, string expected) =>
        Assert.AreEqual(expected, PresentationPreferences.ResolveInitialLanguage(explicitCode, culture));

    [TestMethod]
    public void UserPreferenceStoresOnlyLanguageAndKeepsCorruptOriginalBytes()
    {
        var pathRoot = Path.Combine(Path.GetTempPath(), "ipc-language-" + Guid.NewGuid().ToString("N"));
        var field = typeof(PresentationPreferences).GetField("UserRootForVerification",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        field.SetValue(null, (Func<string>)(() => pathRoot));
        var changed = new List<(string, string)>();
        void Saved(string app, string code) => changed.Add((app, code));
        PresentationPreferences.LanguageChanged += Saved;
        try
        {
            const string app = "sportmaster";
            var path = PresentationPreferences.LanguagePreferenceFile(app);
            Assert.AreEqual("zh-CN", PresentationPreferences.ReadLanguage(app));
            Assert.AreEqual("en-US", PresentationPreferences.ReadLanguage(app, "en-US"));
            Assert.IsFalse(File.Exists(path));
            Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveLanguage(app, "ru"));
            var draft = new SmDisplayPreferenceDraft(app) { Language = "ru-RU" };
            Assert.AreEqual(nameof(draft.Language), draft.Changes().Single().Path);
            Assert.AreEqual("Saved", draft.Save().Single().State);
            Assert.IsFalse(draft.HasChanges);
            Assert.AreEqual("ru-RU", PresentationPreferences.ReadLanguage(app));
            Assert.AreEqual("zh-CN", PresentationPreferences.ReadLanguage("direct-plc"));
            PresentationPreferences.RefreshSaved(app, "language");
            CollectionAssert.AreEqual(new[] { (app, "ru-RU"), (app, "ru-RU") }, changed);
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!));
            foreach (var bytes in new[] { "{broken"u8.ToArray(), """{"Schema":1,"Language":"x-custom"}"""u8.ToArray(), new byte[16384] })
            {
                File.WriteAllBytes(path, bytes);
                Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.ReadLanguage(app));
                Assert.ThrowsExactly<InvalidDataException>(() => PresentationPreferences.SaveLanguage(app, "zh-CN"));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
                Assert.HasCount(2, changed);
            }
        }
        finally
        {
            PresentationPreferences.LanguageChanged -= Saved;
            field.SetValue(null, original);
            if (Directory.Exists(pathRoot)) Directory.Delete(pathRoot, recursive: true);
        }
    }

    [TestMethod]
    public void QuietInstallerAcceptsOnlySupportedLanguageCodes()
    {
        Assert.IsNull(InstallerCommandLine.Parse(["/quiet"]).Language);
        foreach (var code in PresentationPreferences.SupportedLanguages)
        {
            Assert.AreEqual(code, InstallerCommandLine.Parse(["/quiet", "--lang", code]).Language);
            Assert.AreEqual(code, InstallerCommandLine.Parse(["/quiet", "--lang=" + code]).Language);
        }
        foreach (var invalid in new[]
        {
            new[] { "--lang" }, ["--lang", "ru"], ["--lang=", "/norestart"], ["--lang", "RU-RU"],
            ["--lang", "ru-RU", "--lang=en-US"], ["--lang", "/norestart"]
        })
            Assert.ThrowsExactly<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", .. invalid]));
    }
}
