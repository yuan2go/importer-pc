using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    static bool CancellableMaintenance(string action) => action is "Backup" or "Restore" or "Uninstall" or "PlanChange" or "PrepareChange" or "ReviewDeployment" or "Install" or "InstallWithPreflight" or "RepairSystem";
    sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    internal sealed record MaintenancePipeReply(string? Terminal, bool CancelAccepted);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint processId);
    static async Task<string?> ReadMaintenanceLine(TextReader input,CancellationToken token,int maximum=65536)
    {
        var line=new StringBuilder();var character=new char[1];
        while(await input.ReadAsync(character.AsMemory(),token)!=0)
        {
            if(character[0]=='\n')return line.ToString();
            if(line.Length==maximum)throw new IOException("维护帧超出预算");
            line.Append(character[0]);
        }
        if(line.Length!=0)throw new IOException("维护帧被截断");
        return null;
    }
    static void RequireMaintenanceEnvelope(JsonElement message,string hash)
    {
        if(message.ValueKind!=JsonValueKind.Object || message.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=message.EnumerateObject().Count() ||
            message.GetProperty("RequestSha256").GetString()!=hash)
            throw new InvalidDataException("维护帧身份不匹配");
    }
    static async Task HandleMaintenanceChannel(StartupMaintenanceRequest request,string hash)
    {
        using var pipe=new NamedPipeClientStream(".",request.Pipe!,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        if(request.InitiatorProcessId<=0 || request.InitiatorStartedTicks<=0 || !GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var pid) || pid!=(uint)request.InitiatorProcessId)
            throw new UnauthorizedAccessException("维护发起进程身份不匹配");
        using var initiator=Process.GetProcessById(request.InitiatorProcessId);_=initiator.Handle;
        if(initiator.StartTime.ToUniversalTime().Ticks!=request.InitiatorStartedTicks)throw new UnauthorizedAccessException("维护发起进程已变化");
        // Keep the launched helper alive and prohibit work until the caller has
        // authenticated its PID/image/token (desktop ShellExecute returns no handle).
        using (var handshake = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var accepted = new byte[1]; await pipe.ReadExactlyAsync(accepted, handshake.Token);
            if (accepted[0] != 1) throw new UnauthorizedAccessException("维护进程身份尚未确认，未执行操作。");
        }
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false,true),1024,leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,new UTF8Encoding(false,true),true,1024,leaveOpen:true);
        var writes=new object();Exception? protocolFailure=null;var observations=0;
        void Send(object value,bool observation=false)
        {
            lock(writes)
            {
                if(observation && ++observations>1900){protocolFailure??=new IOException("维护观察帧超出预算");return;}
                try { writer.WriteLine(JsonSerializer.Serialize(value)); }
                catch(IOException error) when(observation){protocolFailure??=error;}
            }
        }
        using var execution=CancellableMaintenance(request.Action)?new MaintenanceExecution(new ImmediateProgress<MaintenanceProgress>(p=>
            Send(new{Status="MaintenanceProgress",RequestSha256=hash,Stage=p.Stage.ToString(),p.ProcessedBytes,p.TotalBytes,p.CanCancel},true))):null;
        using var controlLifetime=new CancellationTokenSource();
        var decision=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ReadControls()
        {
            var commands=0;var initial=false;
            try
            {
                while(await ReadMaintenanceLine(reader,controlLifetime.Token,4096) is { } line)
                {
                    using var document=JsonDocument.Parse(line);var frame=document.RootElement;RequireMaintenanceEnvelope(frame,hash);
                    if(++commands>8 || frame.EnumerateObject().Any(p=>p.Name is not ("Command" or "RequestSha256")))throw new InvalidDataException("维护控制帧超出合同");
                    var command=frame.GetProperty("Command").GetString();
                    if(command=="Begin" && execution is not null && !initial){initial=true;decision.TrySetResult();continue;}
                    if(command!="Cancel")throw new InvalidDataException("未知或重复的维护决定");
                    var accepted=execution?.TryCancel()==true;
                    Send(new{Status="CancelAck",RequestSha256=hash,Decision=accepted?"Accepted":"TooLate"});
                    if(execution is not null && !initial){initial=true;decision.TrySetResult();}
                }
                throw new IOException("维护控制通道关闭，结果须核对");
            }
            catch(OperationCanceledException) when(controlLifetime.IsCancellationRequested) { }
            catch(Exception error)
            {
                lock(writes)protocolFailure??=error;
                execution?.TryCancel(); // Stop preparation if still possible; never report this as acknowledged user cancellation.
                decision.TrySetException(error);
            }
        }
        Task? controls=null;string? result=null,errorText=null;var status="Completed";
        try
        {
            MachineRuntimePolicy.RequireExtensionInstallerAuthority();
            if(RequiresManagementMode(request.Action))
            {
                var authority=LocalManagementMode.ForRoot(request.Launch?.DataRoot??throw new InvalidDataException("Missing administrator root"));
                var grant=request.ManagementGrant??throw new UnauthorizedAccessException("管理模式授权缺失，Windows资格不能代替管理密码。");
                var requestHash=ManagementRequestHash(request);authority.ValidateDelegation(grant,requestHash,true);
                delegatedPrincipal.Value="management-mode."+grant.SessionId;
                delegatedAuthority.Value=()=>authority.ValidateDelegation(grant,requestHash,false);
            }
            if(!MediaCommand(request.Action))RequireProtectedHelper(AppContext.BaseDirectory);
            controls=ReadControls();
            if(execution is not null)
            {
                Send(new{Status="Ready",RequestSha256=hash});
                await decision.Task.WaitAsync(TimeSpan.FromSeconds(10));
                execution.Checkpoint();
            }
            result=await ExecuteAsync(request,stage=>Send(new{Status="Progress",RequestSha256=hash,Stage=stage.ToString()},true),execution);
            execution?.Complete();
        }
        catch(OperationCanceledException) when(execution is {CancellationRequested:true,Committing:false})
        {status="Cancelled";Environment.ExitCode=3;}
        catch(Exception error){status="Failed";errorText=error.Message;Environment.ExitCode=2;}
        finally
        {
            execution?.Finish(); // No late Cancel may be accepted after an outcome is fixed.
            controlLifetime.Cancel();if(controls is not null)await controls;
            delegatedAuthority.Value=null;delegatedPrincipal.Value=null;
        }
        if(protocolFailure is not null){status="Unknown";errorText="维护控制或观察通道未完成可信闭环，保留原件及回执核对。";Environment.ExitCode=2;}
        Send(new{Status=status,RequestSha256=hash,Error=errorText is null?null:errorText[..Math.Min(errorText.Length,4000)],Result=result});
    }
    internal static void DisposeMaintenanceWriter(StreamWriter writer,bool terminalCandidate,bool aborted)
    {
        try{writer.Dispose();}
        catch(Exception error)when((terminalCandidate||aborted)&&error is IOException or ObjectDisposedException){}
    }
    // PID/source verification stays in the caller. This exchange owns only this authenticated connection.
    internal static async Task<MaintenancePipeReply> ExchangeMaintenanceResponse(PipeStream pipe,string hash,bool requireReady,
        Action<WindowsInstallationStage> installation,Action<MaintenanceProgress> maintenance,CancellationToken operationToken,CancellationToken lifetime)
    {
        using var input=new StreamReader(pipe,new UTF8Encoding(false,true),true,1024,true);
        var output=new StreamWriter(pipe,new UTF8Encoding(false,true),1024,true){AutoFlush=true};
        using var sending=new CancellationTokenSource();using var sendGate=new SemaphoreSlim(1,1);
        var readySignal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelSignal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registered=operationToken.Register(()=>cancelSignal.TrySetResult());
        var initialSent=false;var cancelSent=false;var aborted=false;MaintenancePipeReply? reply=null;
        async Task SendDecision(bool initial)
        {
            await sendGate.WaitAsync(sending.Token);
            try
            {
                if(initial&&initialSent||cancelSent)return;
                var cancel=!initial||operationToken.IsCancellationRequested;initialSent=true;cancelSent=cancel;
                await output.WriteLineAsync(JsonSerializer.Serialize(new{Command=cancel?"Cancel":"Begin",RequestSha256=hash}).AsMemory(),sending.Token);
            }
            finally{sendGate.Release();}
        }
        async Task SendCancellation()
        {await readySignal.Task.WaitAsync(sending.Token);await cancelSignal.Task.WaitAsync(sending.Token);await SendDecision(false);}
        var cancellationSend=SendCancellation();
        try
        {
            reply=await ReadBoundMaintenanceResponse(input,hash,requireReady,installation,maintenance,async()=>
            {readySignal.TrySetResult();maintenance(new(MaintenanceStage.Checking,null,null,true));await SendDecision(true);},lifetime);
            if(reply.Terminal is null)throw new IOException("维护连接结束但没有终态回执");
            return reply;
        }
        catch{aborted=true;pipe.Dispose();throw;}
        finally
        {
            sending.Cancel();
            try
            {
                try{await cancellationSend;}
                catch(OperationCanceledException)when(sending.IsCancellationRequested){}
                catch(Exception error)when((aborted||reply?.Terminal is not null)&&error is IOException or ObjectDisposedException){}
            }
            finally{DisposeMaintenanceWriter(output,reply?.Terminal is not null,aborted);}
        }
    }
    internal static async Task<MaintenancePipeReply> ReadBoundMaintenanceResponse(TextReader input,string hash,bool requireReady,
        Action<WindowsInstallationStage> installation,Action<MaintenanceProgress> maintenance,Func<Task> ready,CancellationToken token)
    {
        var events=0;var seenReady=false;var accepted=false;
        while(await ReadMaintenanceLine(input,token) is { } line)
        {
            if(++events>2048)throw new IOException("维护帧总量超出预算");
            using var document=JsonDocument.Parse(line);var frame=document.RootElement;RequireMaintenanceEnvelope(frame,hash);
            var status=frame.GetProperty("Status").GetString();
            if(status=="Ready")
            {
                if(!requireReady||seenReady)throw new IOException("维护Ready顺序无效");
                seenReady=true;await ready();continue;
            }
            if(status=="Progress")
            {
                if(requireReady&&!seenReady)throw new IOException("维护进度先于Ready");
                if(!Enum.TryParse<WindowsInstallationStage>(frame.GetProperty("Stage").GetString(),out var stage)||!Enum.IsDefined(stage))throw new IOException("维护安装阶段无效");
                installation(stage);continue;
            }
            if(status=="MaintenanceProgress")
            {
                if(!seenReady||!Enum.TryParse<MaintenanceStage>(frame.GetProperty("Stage").GetString(),out var stage)||!Enum.IsDefined(stage))throw new IOException("维护阶段无效");
                long? ReadCount(string name)=>frame.GetProperty(name).ValueKind==JsonValueKind.Null?null:frame.GetProperty(name).GetInt64();
                var done=ReadCount("ProcessedBytes");var total=ReadCount("TotalBytes");
                if(done<0||total<0||done>total)throw new IOException("维护处理量无效");
                maintenance(new(stage,done,total,frame.GetProperty("CanCancel").GetBoolean()));continue;
            }
            if(status=="CancelAck")
            {
                if(!seenReady)throw new IOException("取消回执先于Ready");
                var value=frame.GetProperty("Decision").GetString();if(value is not ("Accepted" or "TooLate"))throw new IOException("取消回执无效");
                accepted|=value=="Accepted";maintenance(new(value=="Accepted"?MaintenanceStage.CancelAccepted:MaintenanceStage.CancelTooLate,null,null,false));continue;
            }
            if(status is not ("Completed" or "Failed" or "Unknown" or "Cancelled"))throw new IOException("未知维护终态");
            if(status is "Failed" or "Unknown" && (!frame.TryGetProperty("Error",out var error)||error.ValueKind!=JsonValueKind.String))throw new IOException("维护失败回执缺少原因");
            if(requireReady&&!seenReady&&status is "Completed" or "Cancelled")throw new IOException("维护终态缺少开始决定");
            return new(line,accepted);
        }
        return new(null,accepted);
    }
    internal static string? CompleteBoundMaintenanceResponse(MaintenancePipeReply reply,int exitCode,bool cancellationRequested)
    {
        if(reply.Terminal is not null)
        {
            using var document=JsonDocument.Parse(reply.Terminal);
            var status=document.RootElement.GetProperty("Status").GetString();
            if(reply.CancelAccepted&&status=="Completed")throw new InstallationOutcomeUnknownException("已接受取消却报告完成，保留原件核对。");
            if(status=="Cancelled")
            {
                if(exitCode!=3||!reply.CancelAccepted||!cancellationRequested)throw new InstallationOutcomeUnknownException("取消未形成可信终态，请核对原记录。");
                throw new MaintenanceCancelledException("维护已在提交前取消；已生成的备份、未完成副本或隔离目录保留，请核对后重新选择目标或计划。");
            }
        }
        return CompleteMaintenanceResponse(reply.Terminal,exitCode);
    }
}
