using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace Ipc.Application.ParcelFlow.Desktop;

public static class ConfigurationCloseGuard
{
    public static bool HasUnsaved(ParcelViewModel vm, MaintenancePage page)
    {
        var invalid = false;
        foreach (var node in Descendants(page))
        {
            if (node is TextBox text && !text.IsReadOnly) text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (node is DataGrid grid)
                invalid |= !grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true);
            invalid |= Validation.GetHasError(node);
        }
        return invalid || vm.HasUnsavedConfiguration;
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
}
