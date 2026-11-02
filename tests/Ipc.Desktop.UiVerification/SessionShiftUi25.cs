using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
static partial class Program
{
    static async Task VerifySessionShift25(string root, string evidence, List<string> lines)
    {
        void Check(bool ok,string why) { if(!ok)throw new InvalidDataException(why);lines.Add("PASS SS25-WPF "+why); }
        async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        async Task Until(Func<bool> ready) { for(var i=0;i<400;i++){await Flush();if(ready())return;await Task.Delay(25);}throw new TimeoutException("SS25 native state"); }
        await using var h=new ManualSupplyHarness(root,Path.Combine(evidence,"session25"),true);await h.StartAsync();
        var api=(IStationSessionStatistics)h.App;var id=(await api.ReadSessionStatisticsAsync(new(StationReportRange.CurrentShift,false),default)).Shift!.Id;
        var accepted=await h.App.SubmitParcelBarcodeAsync(await h.Command("left","SYNTH-UI-SHIFT"),default);
        await CameraRoundHarness.Until(async()=> (await h.App.OperationAsync(accepted.OperationId!))?.Wcs?.State==StageState.Accepted);
        h.Peer.Mode="reject";var rejected=await h.App.SubmitParcelBarcodeAsync(await h.Command("right","SYNTH-UI-REJECT"),default);
        await CameraRoundHarness.Until(async()=> (await h.App.OperationAsync(rejected.OperationId!))?.Wcs?.State==StageState.Rejected);
        var window=new SupplyStationWindow(h.App,null,"standard");window.Show();await Until(()=>window.ViewModel.Fresh);
        bool Has(string text)=>Descendants(window).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text.Contains(text));
        async Task Click(string text) { await Until(()=>window.ViewModel.CanStartOperation);var b=Descendants(window).OfType<Button>().Single(b=>b.IsVisible&&Equals(b.Content,text));Check(b.IsEnabled,"action enabled: "+text);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(); }
        try
        {
            await SetClient(window,1366,768);await Click("统计与班次");await Until(()=>Has("运行中")&&Has("读取于"));
            var cards=Descendants(window).OfType<TextBlock>().Where(t=>new[]{"识别次数","请求次数","取得格口","拒绝次数","格口未确认","取得格口比例","平均取得格口/小时"}.Contains(AutomationProperties.GetName(t))).ToDictionary(t=>AutomationProperties.GetName(t),t=>t.Text);
            Check(cards.Count==7&&cards["请求次数"]=="2"&&cards["取得格口"]=="1"&&cards["拒绝次数"]=="1"&&cards["识别次数"]=="0","seven cards bind actual original request facts without inventing recognition or physical completion");
            Check(Has("RCS请求")&&Has("RCS拒绝"),"reason groups retain typed RCS rejection separately from observations");
            Check(Descendants(window).OfType<DataGrid>().Single(g=>g.IsVisible).Items.Count==2,"hour chart has a keyboard-readable data table for both positions");
            foreach(var size in new[]{(1366,768),(1092,614),(800,420)}) {await SetClient(window,size.Item1,size.Item2);await Flush();CaptureInternal(evidence,"ss25-statistics-"+size.Item1,(Grid)window.FindName("ClientArea"),lines);}
            await SetClient(window,1366,768);var range=Descendants(window).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="统计时间范围");range.SelectedIndex=1;await Until(()=>Has("尚无上一班记录"));
            Check(Has("还没有上一班"),"previous shift has an explicit empty state on the first business run");
            range.SelectedIndex=0;await Until(()=>Has("运行中"));
            await Click("异常处理");await Until(()=>Has("当前 0 条"));
            var category=Descendants(window).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="异常类别");category.SelectedIndex=1;await Until(()=>Has("当前 1 条"));
            var grid=Descendants(window).OfType<DataGrid>().Single(g=>g.IsVisible);grid.SelectedIndex=0;await Flush();
            Check(Has(rejected.OperationId!)&&Has("SYNTH-UI-REJECT"),"selected exception exposes the exact original operation and barcode");
            var restore=Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,"打开恢复操作…"));Check(!restore.IsEnabled,"ordinary mode cannot start recovery and rejection never advertises resend");
            var selection=grid.SelectedItem;await Task.Delay(2200);await Until(()=>!category.IsEnabled||grid.Items.Count==1);await Flush();
            Check(grid.SelectedItem is not null&&Has(rejected.OperationId!),"bounded automatic refresh preserves selected original identity");
            foreach(var size in new[]{(1366,768),(1092,614),(800,420)}) {await SetClient(window,size.Item1,size.Item2);await Flush();CaptureInternal(evidence,"ss25-exceptions-"+size.Item1,(Grid)window.FindName("ClientArea"),lines);}
            await SetClient(window,1366,768);await Click("查看详情与核对…");await Until(()=>window.ViewModel.SelectedPage=="history"&&window.ViewModel.Selected?.Id==rejected.OperationId);
            Check(window.ViewModel.Selected!.Wcs!.Request.OperationId==rejected.OperationId,"details resolves the selected original rather than the current work position");
            var second=new SupplyStationWindow(h.App,null,"standard");second.Show();await Flush();second.Close();await Flush();
            Check((await api.ReadSessionStatisticsAsync(new(StationReportRange.CurrentShift,false),default)).Shift!.Id==id,"multiple windows, page changes and a window close share the same durable business shift");
            Check(h.Peer.Requests.Count==2,"view, refresh, range changes and original details never send another RCS request");
        }
        finally { window.ConfirmDiscardDrafts=_=>true;window.Close();await Flush(); }
    }
}
