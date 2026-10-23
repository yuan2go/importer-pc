using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;

static partial class Program
{
    static async Task<ParameterSettingsControl> FirstConfigurationReady(Window window, FieldInfo busy, Action<bool, string> check)
    {
        for (var i = 0; i < 200 && ((bool)busy.GetValue(window)! || !Descendants(window).OfType<ParameterSettingsControl>().Any()); i++)
            await Task.Delay(20);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var editors = Descendants(window).OfType<ParameterSettingsControl>().ToArray();
        check(!(bool)busy.GetValue(window)! && editors.Length == 1 && editors[0].Model is not null && editors[0].IsVisible && editors[0].IsEnabled,
            "first configuration finishes asynchronous loading with one visible, enabled shared editor");
        var editor = editors.Single();
        var directory = Descendants(editor).OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "设置目录");
        check(window.Title.StartsWith("首次配置", StringComparison.Ordinal) && editor.FirstConfiguration && editor.SelectedCategory == 1 &&
            directory.Items.Count > 0 && editor.CurrentPageTitle == directory.Items[0].ToString(),
            "first configuration starts at the first business page; actual=" + editor.CurrentPageTitle);
        check(!Descendants(window).OfType<TabControl>().Any(t => t.Name == "StartupTasks"),
            "first configuration uses the shared directory without duplicate outer task tabs");
        check(directory.IsVisible && directory.Items.Groups is { } groups && groups.Cast<CollectionViewGroup>().Select(g => g.Name?.ToString())
            .SequenceEqual(new[] { "业务模块", "系统", "驱动", "组件", "检查" }) && directory.Items.Cast<object>().Count(p => p.ToString() == "检查与完成") == 1,
            "one directory exposes business, system, drivers, components and the completion review in order");
        return editor;
    }

    static void CheckFirstConfigurationReview(Window window, ParameterSettingsControl editor, Action<bool, string> check)
    {
        editor.ShowReview(); window.UpdateLayout();
        check(editor.IsReviewPage && Descendants(window).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "保存配置")) &&
            !Descendants(window).OfType<Button>().Any(b => b.IsVisible && (Equals(b.Content, "保存并启动") || Equals(b.Content, "保存首配待生效…") || Equals(b.Content, "完成"))),
            "completion review offers guarded save; completion requires confirmed persistence and startup remains separate");
    }
}
