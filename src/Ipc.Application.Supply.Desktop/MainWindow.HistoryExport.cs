using System.IO;
using System.Windows;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Win32;

namespace Ipc.Desktop;

public partial class MainWindow
{
    async void ExportHistoryClick(object sender,RoutedEventArgs e)
    {
        if (!BeginHistoricalRead("导出当前筛选",out _) || loadedHistory is null)
        { model.LastResult="请先以有效身份成功查询；导出只使用该查询条件与返回的历史上界。"; return; }
        var selectedQuery=loadedHistory.Query with { Offset=0,Limit=200 };
        var dialog=new SaveFileDialog {Filter="JSON|*.json",FileName="ipc-history-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".json"};
        if(dialog.ShowDialog(this)!=true || !BeginHistoricalRead("导出当前筛选",out var readActor))return;
        await RunAsync(async()=>
        {
            var rows=new List<HistoryEntry>();var identities=new HashSet<string>(StringComparer.Ordinal);
            var query=selectedQuery;int? total=null;var started=DateTimeOffset.UtcNow;
            while(true)
            {
                if(!ReadIdentityMatches(readActor))throw new GuardException("导出身份已失效；未发布文件");
                var page=await Runtime.Application!.QueryHistoryAsync(query).WaitAsync(lifetime.Token);
                if(!ReadIdentityMatches(readActor))throw new GuardException("导出期间身份已失效；未发布文件");
                if(total is not null && total!=page.TotalCount)throw new InvalidOperationException("筛选结果在读取期间发生变化；请重新查询后导出");
                total??=page.TotalCount;
                foreach(var row in page.Items)if(identities.Add(row.Id))rows.Add(row);else throw new InvalidOperationException("历史分页发生变化；未发布不完整导出");
                if(!page.HasNext)break;
                if(page.Items.Length==0)throw new InvalidOperationException("历史分页未前进；未发布不完整导出");
                query=page.Query with {Offset=page.Query.Offset+page.Items.Length};
            }
            if(rows.Count!=total)throw new InvalidOperationException("历史数量已变化；请重新查询后导出");
            var exported = new List<object>();
            foreach (var row in rows)
            {
                if (!ReadIdentityMatches(readActor)) throw new GuardException("导出身份已失效；未发布文件");
                var details = row.Operation is null ? (await Runtime.Application!.SessionDetailsAsync(row.SessionId).WaitAsync(lifetime.Token)).Requests :
                    await Runtime.Application!.DetailsAsync(row.Id).WaitAsync(lifetime.Token);
                if (!ReadIdentityMatches(readActor)) throw new GuardException("导出身份已失效；未发布文件");
                exported.Add(new { Entity = row.Operation is null ? "BoxSession" : "ItemOperation", Entry = row, Requests = details });
            }
            var temporary=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".new";
            try
            {
                await File.WriteAllTextAsync(temporary,Wire.Encode(new {Query=selectedQuery,StartedAt=started,CompletedAt=DateTimeOffset.UtcNow,Count=rows.Count,SnapshotSemantics="Bounded parent identities; current child facts read during export, not an immutable database snapshot",Items=exported}),lifetime.Token);
                if(!ReadIdentityMatches(readActor))throw new GuardException("导出身份已失效；未发布文件");
                File.Move(temporary,dialog.FileName,true);
                HistoryFeedback.Text=$"已导出当前筛选 {rows.Count} 条；已包含原请求及每份接收责任；子事实为读取期间的快照。";
            }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        });
    }
}
