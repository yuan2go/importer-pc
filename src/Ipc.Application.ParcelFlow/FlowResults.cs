using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;
public static class FlowResults
{
    public static ParcelState PlcState(StageFact stage) => stage.State switch
    {
        StageState.Prepared or StageState.Dispatching => ParcelState.SubmittingPlc,
        StageState.Accepted => stage.Replies.Any(x => x.PhysicalCompleted == true) ? ParcelState.PhysicalCompleted : ParcelState.PlcAccepted,
        StageState.Rejected => ParcelState.PlcRejected,
        StageState.Unknown => ParcelState.PlcUnknown,
        _ => ParcelState.Blocked
    };
}
