using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ipc.Desktop;
public partial class LaneControl : UserControl
{
    public LaneControl() => InitializeComponent();
    MainWindow OwnerWindow => (MainWindow)Window.GetWindow(this);
    LaneViewModel Model => (LaneViewModel)DataContext;
    async void ScanClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("ScanRetained", Model.Current?.Id, Model.RetainedDraft.TrimEnd('\r','\n','\t'));
    async void RetainedKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab) || Keyboard.Modifiers != ModifierKeys.None || Model.RetainedDraft.Length == 0) return;
        e.Handled = true;
        if (Model.CanScan) await OwnerWindow.SendAsync("ScanRetained", Model.Current?.Id, Model.RetainedDraft.TrimEnd('\r','\n','\t'));
    }
    async void ClearClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("ClearSplit", Model.Current?.Id, "CONFIRM", "清空当前尚未提交的拆包计数？");
    async void CancelClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("CancelDraft", Model.Current?.Id, confirm: "处置当前未提交草稿？原请求、已接受事实和实体责任继续保留。");
    async void DetailClick(object sender, RoutedEventArgs e) { if (Model.Current is not null) await OwnerWindow.ShowDetailAsync(Model.Current.Id, Model.Current); }
}
