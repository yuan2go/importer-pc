using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Platform;

namespace Ipc.Desktop;

public abstract partial class StartupWorkflowWindow
{
    readonly Border maintenancePanel=new(){Visibility=Visibility.Collapsed,Margin=new(0,8,0,8),Padding=new(8),BorderThickness=new(1),BorderBrush=Brushes.LightSlateGray,Background=Brushes.White};
    readonly TextBox maintenanceReadout=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=54,MaxHeight=140,BorderThickness=new(0),Background=Brushes.Transparent};
    readonly Button maintenanceCancel=new(){Content="请求取消",MinHeight=44,MinWidth=110,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Top};
    readonly TextBlock maintenanceElapsed=new(){Margin=new(0,4,0,0)};
    CancellationTokenSource? maintenanceCancellation;
    long maintenanceGeneration;
    string? unknownMaintenanceContext;
    FrameworkElement CreateMaintenancePanel(bool compact=false)
    {
        if(compact)maintenanceReadout.MaxHeight=90;
        AutomationProperties.SetAutomationId(maintenanceReadout,"Maintenance.Progress");
        AutomationProperties.SetName(maintenanceReadout,"维护对象、阶段和停止影响");
        AutomationProperties.SetLiveSetting(maintenanceReadout,AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(maintenanceCancel,"Maintenance.Cancel");
        var layout=new DockPanel();DockPanel.SetDock(maintenanceCancel,Dock.Right);layout.Children.Add(maintenanceCancel);var content=new StackPanel();content.Children.Add(maintenanceReadout);content.Children.Add(maintenanceElapsed);layout.Children.Add(content);maintenancePanel.Child=layout;
        maintenanceCancel.Click+=(_,_)=>
        {
            if(maintenanceCancellation is null)return;
            maintenanceCancel.IsEnabled=false;
            maintenanceReadout.Text+="\n已请求停止，正在等待授权窗口结束或最终结果；不要重复提交。";
            maintenanceCancellation.Cancel();
        };
        return maintenancePanel;
    }
    protected void ResolveKnownMaintenanceOutcome(){UnknownOutcome=false;unknownMaintenanceContext=null;}
    protected async Task<T> RunMaintenance<T>(string context,Func<IProgress<MaintenanceProgress>,CancellationToken,Task<T>> operation,bool readOnly=false,bool canRequestCancellation=true)
    {
        if(UnknownOutcome&&!readOnly)throw new InstallationOutcomeUnknownException("原维护结果仍待核对；保留原源/目标和回执，不能再次提交维护操作。");
        var originalContext=context;
        if(readOnly&&unknownMaintenanceContext is not null)context="原操作结果待核对：\n"+unknownMaintenanceContext+"\n本次只读检查：\n"+context;
        Message(UnknownOutcome?"原操作仍待核对；只读检查不会解除原提交阻断。":"维护正在执行，请查看当前阶段；原责任继续保留。");
        using var cancellation=new CancellationTokenSource();maintenanceCancellation=cancellation;var generation=++maintenanceGeneration;
        maintenancePanel.Visibility=Visibility.Visible;maintenanceCancel.Visibility=canRequestCancellation?Visibility.Visible:Visibility.Collapsed;maintenanceCancel.IsEnabled=canRequestCancellation;
        maintenanceReadout.Text=(canRequestCancellation?"等待授权与检查；提交前可以请求取消。":"正在核对并继续原操作；此项不提供取消。")+"\n"+context;maintenanceReadout.Focus();
        var elapsed=Stopwatch.StartNew();
        void UpdateElapsed()=>maintenanceElapsed.Text="已等待 "+((long)elapsed.Elapsed.TotalSeconds)+" 秒；等待时间不代表完成比例。";
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>UpdateElapsed();UpdateElapsed();timer.Start();
        var progress=new Progress<MaintenanceProgress>(p=>
        {
            if(generation!=maintenanceGeneration)return;
            var stage=p.Stage switch
            {
                MaintenanceStage.Checking=>"正在核对权限、原任务和目标",
                MaintenanceStage.CopyingSnapshot=>"正在复制原数据检查副本",
                MaintenanceStage.CopyingDatabase=>"正在复制数据库",
                MaintenanceStage.CopyingCustody=>"正在复制恢复所需的原件",
                MaintenanceStage.Verifying=>"正在核验恢复集",
                MaintenanceStage.PublishingBackup=>"正在保存完整备份结果",
                MaintenanceStage.PublishingRestore=>"正在保存隔离恢复结果",
                MaintenanceStage.RetiringDeployment=>"正在提交程序退役",
                MaintenanceStage.RemovingProgram=>"正在清理本次安装的程序与系统入口",
                MaintenanceStage.InstallingMachine=>"已进入机器变更；正在安装程序和系统入口",
                MaintenanceStage.RepairingSystem=>"正在修复原安装的系统入口",
                MaintenanceStage.CancelAccepted=>"已接受取消请求，正在停止并确认结果",
                MaintenanceStage.CancelTooLate=>"已进入提交或完成阶段；取消不能回滚，请等待结果",
                _=>throw new InvalidOperationException("未知维护阶段")
            };
            var count=p.ProcessedBytes is { } done?" · "+done.ToString("N0")+" 字节"+(p.TotalBytes is { } total?" / "+total.ToString("N0"):""):"";
            maintenanceReadout.Text=stage+count+"\n"+(cancellation.IsCancellationRequested?"已请求取消，仍须等待最终结果。":p.CanCancel?"可以请求取消；已生成的文件将保留。":"此阶段不可取消，请等待最终结果。")+"\n"+context;
            maintenanceCancel.IsEnabled=canRequestCancellation&&p.CanCancel&&!cancellation.IsCancellationRequested;
        });
        try
        {
            var result=await Task.Run(()=>operation(progress,cancellation.Token));
            maintenanceReadout.Text=(readOnly?"已取得检查结果。":"已取得完成回执。")+(cancellation.IsCancellationRequested?"取消请求未改变已完成的结果。":"")+"\n"+context;
            return result;
        }
        catch(MaintenanceCancelledException)
        {maintenanceReadout.Text="已在提交前取消；已生成的备份、未完成副本或隔离目录保留。重新执行前核对并选择新的目标或计划。\n"+context;throw;}
        catch(InstallationOutcomeUnknownException)
        {UnknownOutcome=true;unknownMaintenanceContext??=originalContext;maintenanceReadout.Text="结果待核对；保留原件与回执，禁止重复提交。\n"+context;throw;}
        catch
        {maintenanceReadout.Text="操作未完成；保留已生成文件并核对下方原因，不按取消或回滚处理。\n"+context;throw;}
        finally
        {timer.Stop();elapsed.Stop();UpdateElapsed();maintenanceGeneration++;maintenanceCancellation=null;maintenanceCancel.IsEnabled=false;maintenanceCancel.Visibility=Visibility.Collapsed;}
    }
    protected Task RunMaintenance(string context,Func<IProgress<MaintenanceProgress>,CancellationToken,Task> operation)
        => RunMaintenance(context,async(progress,token)=>{await operation(progress,token);return true;});
}
