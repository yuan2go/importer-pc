using System.Windows;
using System.Windows.Controls;

namespace Ipc.Desktop;
public partial class LaneControl : UserControl
{
    public LaneControl() => InitializeComponent();
    MainWindow OwnerWindow => (MainWindow)Window.GetWindow(this);
    LaneViewModel Model => (LaneViewModel)DataContext;
    async void EventClick(object sender, RoutedEventArgs e) => await OwnerWindow.RunAsync(async () =>
    {
        var kind = (string)((Button)sender).Tag;
        if (kind is "Depart" or "Reset" or "Revalidate" && !Confirmation.Ask(OwnerWindow, "确认已提供对应实体的模拟物理事件？这不会抹除旧业务结果。")) return;
        if (!int.TryParse(Model.QuantityDraft, out var quantity)) throw new InvalidOperationException("包装数量必须是整数");
        var codes = kind switch { "NoCode" => Array.Empty<string>(), "MultiCode" => new[] { Model.BarcodeDraft, "999999" }, _ => new[] { Model.BarcodeDraft } };
        await OwnerWindow.Runtime.EmitAsync(Model.Lane, kind is "NoCode" or "MultiCode" ? "Read" : kind, codes, quantity);
    });
    async void ReplayClick(object sender, RoutedEventArgs e) => await OwnerWindow.RunAsync(() => OwnerWindow.Runtime.ReplayAsync(Model.Lane));
    async void SubmitClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("Submit", Model.Current?.Id, confirm: "确认供包？请核对条码、数量与当前实体。");
    async void ScanClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("ScanRetained", Model.Current?.Id, Model.RetainedDraft);
    async void ClearClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("ClearSplit", Model.Current?.Id, "CONFIRM", "清空当前尚未提交的拆包计数？");
    async void CancelClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("CancelDraft", Model.Current?.Id, confirm: "处置当前未提交草稿？未知或在途副作用将由守卫拒绝。");
    async void DetailClick(object sender, RoutedEventArgs e) { if (Model.Current is not null) await OwnerWindow.ShowDetailAsync(Model.Current.Id); }
}
