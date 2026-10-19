using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    DateTimeOffset? lastRead;
    string? readFailure, readFailureMessage;
    Exception? lastReadError;
    bool fresh;
    long appliedReadId, failedReadId;
    long lastReadTick;
    readonly HashSet<Guid> retiredRuntimeIds = [];
    bool MonotonicFresh => lastReadTick != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(lastReadTick) < TimeSpan.FromSeconds(3);
    public bool Fresh => fresh && MonotonicFresh && lastRead is { } at && DateTimeOffset.UtcNow >= at && DateTimeOffset.UtcNow-at < TimeSpan.FromSeconds(3);
    public string UpdatedAt => lastRead is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "尚未取得快照";
    public string ReadStatus => !Fresh ? "快照已失效 · " + (readFailure ?? "正在读取或读取超时") + " · 最后成功读取：" + UpdatedAt : "状态更新时间 · " + UpdatedAt;
    public bool CanQueryCurrent => Fresh && Current is not null;
    protected ParcelSnapshot RequireFreshSnapshot() => Fresh && Snapshot is { } value ? value : throw new InvalidOperationException(ReadStatus);
    public void MarkReadFailure(string reason)
    {
        readFailure=reason; readFailureMessage="状态读取失败，旧快照仅供参考："+reason; Message=readFailureMessage;
        RefreshFreshness();
    }
    void SnapshotReadApplied(long id)
    {
        AdvanceReadId(ref appliedReadId,id); lastRead=DateTimeOffset.UtcNow; lastReadTick=System.Diagnostics.Stopwatch.GetTimestamp(); readFailure=null; lastReadError=null;
        if(Message==readFailureMessage)Message="";
        readFailureMessage=null; RefreshFreshness();
    }
    bool CanApplySnapshot(long id) => id>Volatile.Read(ref failedReadId);
    void SnapshotReadFailed(long id,Exception error)
    {
        lastReadError=error;
        if(id>Volatile.Read(ref appliedReadId) && AdvanceReadId(ref failedReadId,id))MarkReadFailure(error.Message);
    }
    static bool AdvanceReadId(ref long current,long next)
    {
        var previous=Volatile.Read(ref current);
        while(next>previous)
        {
            var actual=Interlocked.CompareExchange(ref current,next,previous);
            if(actual==previous)return true;
            previous=actual;
        }
        return false;
    }
    public void RefreshFreshness(DateTimeOffset? at=null)
    {
        var now=at??DateTimeOffset.UtcNow;
        fresh=MonotonicFresh && lastRead is { } read && now>=read && now-read<TimeSpan.FromSeconds(3) && readFailure is null;
        foreach(var name in new[]{nameof(Fresh),nameof(UpdatedAt),nameof(ReadStatus),nameof(Status),nameof(Health),nameof(BlockingReasons),nameof(CanAdmit),nameof(CanResume),nameof(CanConfigure),nameof(CanContinue),nameof(CanContinueUpload),nameof(CanAbandon)})Changed(name);
        NotifyTargets(); SnapshotValidityChanged(now);
    }
    protected virtual void SnapshotValidityChanged(DateTimeOffset now) { }
}
