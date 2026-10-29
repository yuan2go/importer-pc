using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

static class HistorySessionContractChecks
{
    public static async Task Run()
    {
        var old = DispatchProxy.Create<IParcelApplication, UnsupportedHistoryProbe>();
        var probe = (UnsupportedHistoryProbe)(object)old;
        var model = new ParcelViewModel(old, "历史查询契约验证", "sportmaster");
        var refused = false;
        try { await model.LoadHistoryAsync(); }
        catch (NotSupportedException) { refused = true; }
        if (!refused || probe.Calls != 0 || model.HistoryLoading || model.HistoryItems.Length != 0)
            throw new Exception("Unsupported history provider entered a legacy query or left a loading owner behind");
        Console.WriteLine("PASS current history contract: missing cancellable session refuses without invoking old history/metrics APIs or leaking read ownership");
    }
}

public class UnsupportedHistoryProbe : DispatchProxy
{
    public int Calls;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    { Calls++; throw new InvalidOperationException("Unsupported provider must not be called: " + method?.Name); }
}

public interface ICurrentHistoryFixtureForwarder : IParcelHistorySessionApplication
{
    IParcelHistorySessionApplication HistoryService { get; }
    Task<ParcelHistorySelection> IParcelHistoryApplication.SelectHistoryAsync(ParcelHistoryFilter filter) => HistoryService.SelectHistoryAsync(filter);
    Task<ParcelExportReceipt> IParcelHistoryApplication.ExportHistoryAsync(ParcelHistorySelection selection, string destination, CancellationToken token) => HistoryService.ExportHistoryAsync(selection, destination, token);
    Task<ParcelHistoryPage> IParcelHistorySessionApplication.ReadHistoryPageAsync(ParcelHistorySelection selection, long beforeSequence, int limit, CancellationToken token) => HistoryService.ReadHistoryPageAsync(selection, beforeSequence, limit, token);
    Task<ParcelHistoryMetrics> IParcelHistorySessionApplication.ReadHistoryMetricsAsync(ParcelHistorySelection selection, CancellationToken token) => HistoryService.ReadHistoryMetricsAsync(selection, token);
    Task<string[]> IParcelHistorySessionApplication.HistoryPositionIdsAsync(ParcelHistorySelection selection, CancellationToken token) => HistoryService.HistoryPositionIdsAsync(selection, token);
}
