using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Ipc.Application.Contracts;
namespace Ipc.Application.Sportmaster.Desktop;

// One bounded read per Application, shared by all its windows. UI never increments counts.
internal sealed class StationPace
{
    static readonly ConditionalWeakTable<IParcelApplication, StationPace> owners = new();
    readonly object gate = new(); Task<Value>? pending; long requested;
    internal sealed record Value(long At, long Total, long Left, long Right, double PerMinute);
    public static Task<Value> Read(IParcelApplication application)
    {
        var owner = owners.GetValue(application, _ => new());
        lock (owner.gate)
        {
            if (owner.pending is { IsCompleted: false } || owner.pending is not null && Stopwatch.GetElapsedTime(owner.requested) < TimeSpan.FromSeconds(2)) return owner.pending;
            owner.requested = Stopwatch.GetTimestamp(); return owner.pending = owner.Load(application);
        }
    }
    async Task<Value> Load(IParcelApplication application)
    {
        if (application is not IStationOperations api) throw new NotSupportedException("当前业务没有统计能力");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var now = DateTimeOffset.UtcNow;
        var policy = await api.ReadOperationsPolicyAsync().WaitAsync(budget.Token);
        var shift = StationShiftSchedule.Current(policy, now);
        var to = new DateTimeOffset(now.UtcDateTime.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero).AddMinutes(1);
        var from = shift.From < to.AddMinutes(-10) ? shift.From : to.AddMinutes(-10);
        var result = await api.ReadStatisticsAsync(from, to, budget.Token);
        if (Ipc.Platform.Wire.Encode(result.Policy) != Ipc.Platform.Wire.Encode(policy)) throw new InvalidOperationException("统计策略已变化，请重新读取");
        var current = result.Buckets.Where(b => b.Minute >= shift.From).ToArray();
        var recent = result.Buckets.Where(b => b.Minute >= to.AddMinutes(-10)).Sum(b => b.Assigned);
        return new(Stopwatch.GetTimestamp(), current.Sum(b => b.Assigned), current.Where(b => b.Position == "left").Sum(b => b.Assigned), current.Where(b => b.Position == "right").Sum(b => b.Assigned), recent / 10d);
    }
}

public sealed partial class SupplyStationViewModel
{
    string paceText = "本班取得格口 —", paceCompact = "本班 —", leftPace = "本位本班取得格口 —", rightPace = "本位本班取得格口 —";
    bool showPace = true;
    public Visibility PaceVisibility => showPace && SelectedPage == "workstation" ? Visibility.Visible : Visibility.Collapsed;
    public string PaceText => paceText;
    public string PaceCompact => paceCompact;
    public string LeftPaceText => leftPace;
    public string RightPaceText => rightPace;
    internal void SetPace(StationPace.Value? value)
    {
        var valid = Fresh && value is not null && Stopwatch.GetElapsedTime(value.At) < TimeSpan.FromSeconds(6);
        paceText = valid ? $"本班取得格口 {value!.Total:N0} 次 · 近10分钟 {value.PerMinute:F1} 次/分" : "本班取得格口 — · 近10分钟 —";
        paceCompact = valid ? $"本班 {value!.Total:N0} 次" : "本班 —";
        leftPace = valid ? $"本位本班取得格口 {value!.Left:N0} 次" : "本位本班取得格口 —";
        rightPace = valid ? $"本位本班取得格口 {value!.Right:N0} 次" : "本位本班取得格口 —";
        foreach (var name in new[] { nameof(PaceVisibility), nameof(PaceText), nameof(PaceCompact), nameof(LeftPaceText), nameof(RightPaceText) }) Changed(name);
    }
    internal void NotifyPaceVisibility() => Changed(nameof(PaceVisibility));
    internal void SetPaceVisibility(bool visible) { showPace = visible; Changed(nameof(PaceVisibility)); }
}
