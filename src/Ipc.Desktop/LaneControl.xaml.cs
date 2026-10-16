using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ipc.Supply;

namespace Ipc.Desktop;
public partial class LaneControl : UserControl
{
    public LaneControl() => InitializeComponent();
    MainWindow OwnerWindow => (MainWindow)Window.GetWindow(this);
    LaneViewModel Model => (LaneViewModel)DataContext;
    public bool ReviewFits => IsVisible && ActualWidth>0 && CriticalScroll.ViewportHeight>0 && CriticalScroll.ScrollableHeight<0.5 && CriticalScroll.ScrollableWidth<0.5;
    public void ApplyDensity(bool compact)
    {
        ResultTitle.FontSize=compact?23:29; BarcodeValue.FontSize=compact?23:27;
        PackageValue.FontSize=DemandValue.FontSize=SupplyValue.FontSize=compact?26:31;
        PrimaryAction.MinHeight=compact?48:52;
        SecondaryActions.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        Grid.SetRow(QuantityPanel,compact?0:1); Grid.SetRow(Metadata,compact?1:0);
    }
    void CriticalScrollChanged(object sender,ScrollChangedEventArgs e)
    {
        if (IsLoaded && Window.GetWindow(this) is MainWindow owner) owner.ReviewGeometryChanged();
    }
    async void PrimaryClick(object sender,RoutedEventArgs e)
    {
        if (Model.Current?.Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired) await OwnerWindow.SubmitLaneAsync(Model);
        else if(Model.Current is { } current) await OwnerWindow.ShowDetailAsync(current.Id,current);
    }
    async void ScanClick(object sender, RoutedEventArgs e) => await ScanAsync();
    async Task ScanAsync()
    {
        if (!Model.CanScan || Model.Current is not { } current) return;
        await OwnerWindow.SendAsync("ScanRetained",current.Id,Model.RetainedDraft.TrimEnd('\r','\n','\t'));
    }
    async void RetainedKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab) || Keyboard.Modifiers != ModifierKeys.None || Model.RetainedDraft.Length == 0) return;
        e.Handled = true; await ScanAsync();
    }
    async void ClearClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("ClearSplit", Model.Current?.Id, "CONFIRM", $"清空 {Model.Label} / {Model.Current?.Id} / r{Model.Current?.Revision} 的 {Model.SplitCount} 个保留件计数？另一通道、历史与已接受账保留。");
    async void CancelClick(object sender, RoutedEventArgs e) => await OwnerWindow.SendAsync("CancelDraft", Model.Current?.Id, confirm: $"处置 {Model.Label} / {Model.Current?.Id} / r{Model.Current?.Revision} 未提交草稿？原请求、已接受事实与实体责任继续保留。");
    async void DetailClick(object sender, RoutedEventArgs e) { if (Model.Current is not null) await OwnerWindow.ShowDetailAsync(Model.Current.Id, Model.Current); }
    void CopyBarcodeClick(object sender,RoutedEventArgs e)
    {
        try { Clipboard.SetText(Model.Barcode); }
        catch(System.Runtime.InteropServices.ExternalException) { BarcodeValue.Focus(); BarcodeValue.SelectAll(); }
    }
}
