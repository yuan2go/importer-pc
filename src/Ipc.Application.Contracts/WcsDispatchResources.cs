namespace Ipc.Application.Contracts;

// Resource admission precedes Application's final round/authority check and T2.
// A permit is local capacity, never proof of a body write or remote receipt.
public interface IWcsDispatchResources
{
    WcsDispatchProgress DispatchProgress { get; }
    Task<IWcsDispatchPermit> ReserveDispatchAsync(FrozenRequest request, CancellationToken token);
}
public sealed record WcsDispatchProgress(int Capacity, int Occupied, int ActiveSends, int LeftWaiting, int RightWaiting);
public interface IWcsDispatchPermit : IDisposable
{
    Task<StageReply> SendAsync(CancellationToken token);
}
