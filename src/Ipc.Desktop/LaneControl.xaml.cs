using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ipc.Supply;

namespace Ipc.Desktop;
public partial class LaneControl : UserControl
{
    public LaneControl() => InitializeComponent();
    MainWindow OwnerWindow => (MainWindow)Window.GetWindow(this);
    LaneViewModel Model => (LaneViewModel)DataContext;
    public bool ReviewFits
    {
        get
        {
            if (!IsLoaded || !IsVisible || ActualWidth<=0 || ActualHeight<=0) return false;
            var viewport=FindViewport(CriticalScroll);
            if (viewport is null || viewport.ActualWidth<=0 || viewport.ActualHeight<=0) return false;
            // Only required facts authorize a command. Expanded source/history content may
            // overflow independently; scrolling a required fact out of view revokes review.
            return FullyVisible(BarcodeReview,viewport) && FullyVisible(Metadata,viewport) &&
                FullyVisible(QuantityPanel,viewport) && FullyVisible(QuantityUnitLabel,viewport) && (!Model.SplitVisible || FullyVisible(SplitReview,viewport)) &&
                (!Model.HasWaveDifference || FullyVisible(WaveDifferenceReview,this)) &&
                FullyVisible(PrimaryAction,this) && FullyVisible(SecondaryActions,this);
        }
    }
    static ScrollContentPresenter? FindViewport(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is ScrollContentPresenter viewport)return viewport;
            if(FindViewport(child) is { } nested)return nested;
        }
        return null;
    }
    static bool FullyVisible(FrameworkElement element,FrameworkElement viewport)
    {
        if(!element.IsVisible || element.ActualWidth<=0 || element.ActualHeight<=0)return false;
        var bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
        const double tolerance=0.5;
        return bounds.Left>=-tolerance && bounds.Top>=-tolerance &&
            bounds.Right<=viewport.ActualWidth+tolerance && bounds.Bottom<=viewport.ActualHeight+tolerance;
    }
    public void ApplyDensity(bool compact)
    {
        ResultTitle.FontSize=compact?23:29; BarcodeValue.FontSize=compact?23:27;
        PackageValue.FontSize=DemandValue.FontSize=SupplyValue.FontSize=compact?26:31;
        PrimaryAction.MinHeight=compact?48:52;
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
