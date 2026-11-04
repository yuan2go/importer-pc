using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public bool SelectedRcsQueryUnavailable => Selected?.Wcs is { State: StageState.Unknown or StageState.Conflict, Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } &&
        !new[] { Selected.Plc, Selected.Capture, Selected.Upload }.OfType<StageFact>().Any(s => s.State == StageState.Unknown);
    public string HistoryQueryGuidance => SelectedRcsQueryUnavailable ? "取格口结果未确认。原请求已保留，不会自动重发。" : "";
}
