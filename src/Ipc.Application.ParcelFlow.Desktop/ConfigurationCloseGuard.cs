using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace Ipc.Application.ParcelFlow.Desktop;

public static class ConfigurationCloseGuard
{
    public static bool HasUnsaved(ParcelViewModel vm, MaintenancePage page)
        => CommitAndFindInvalid(page) is not null || vm.HasUnsavedConfiguration;
    public static FrameworkElement? CommitAndFindInvalid(DependencyObject root, bool enabledOnly = false)
    {
        FrameworkElement? first = null;
        foreach (var node in Descendants(root))
        {
            if (enabledOnly && node is UIElement { IsEnabled: false }) continue;
            if (node is TextBox text && !text.IsReadOnly) text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (node is DataGrid grid)
                if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) first ??= grid;
            if (Validation.GetHasError(node) && node is FrameworkElement field && (first is null || first is not TextBox && field is TextBox)) first = field;
        }
        return first;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(); var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.TryPop(out var item))
        {
            if (!seen.Add(item)) continue;
            yield return item;
            foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>()) pending.Push(child);
            if (item is Visual or Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) pending.Push(VisualTreeHelper.GetChild(item, i));
        }
    }
    public static bool ConfirmDiscard(Window owner, int count) => MessageBox.Show(owner,
        "有 " + count + " 个窗口包含未保存或尚未通过校验的配置输入。\n选择“确定”放弃这些草稿并继续关闭；选择“取消”返回编辑。不会自动保存或激活配置。",
        "未保存的配置草稿", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    public static Task<bool> ReviewUnsavedAsync(Window owner, int count, Func<Task>? save, string action)
    {
        var dialog = new Window { Owner = owner, Title = "保留未完成设置", Width = 620, SizeToContent = SizeToContent.Height, MaxHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var body = new StackPanel { Margin = new(24) };
        body.Children.Add(new TextBlock { Text = count + " 个窗口有未保存输入。请选择如何继续" + action + "。配置保存、草稿保留与运行生效是不同操作。", TextWrapping = TextWrapping.Wrap, FontSize = 17 });
        var status = new TextBlock { Text = count > 1 ? "多个窗口可能包含不同候选。请返回后逐窗核对和保留；不会合并或覆盖它们。" : "加密保留可包含无效输入；不写入待生效配置，也不启动设备。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 16) };
        body.Children.Add(status);
        var actions = new WrapPanel();
        var back = new Button { Content = "继续编辑", IsCancel = true, MinHeight = 44, Margin = new(0, 0, 8, 8) };
        var keep = new Button { Content = "加密保留后" + action, IsEnabled = count == 1 && save is not null, MinHeight = 44, Margin = new(0, 0, 8, 8) };
        var discard = new Button { Content = "放弃未保存输入并" + action, MinHeight = 44, Margin = new(0, 0, 8, 8) };
        bool busy = false;
        back.Click += (_, _) => dialog.DialogResult = false; discard.Click += (_, _) => dialog.DialogResult = true;
        keep.Click += async (_, _) =>
        {
            busy = true; actions.IsEnabled = false;
            try { await save!(); busy = false; dialog.DialogResult = true; }
            catch (Exception ex) { status.Text = "草稿未能保留：" + ex.Message + "。当前输入仍在，请继续编辑或检查维护资格。"; }
            finally { busy = false; actions.IsEnabled = true; }
        };
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };
        actions.Children.Add(back); actions.Children.Add(keep); actions.Children.Add(discard); body.Children.Add(actions); dialog.Content = body;
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
