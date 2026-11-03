using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Optional offline update notice for the maintenance page. Reading never installs or blocks work.
public interface IUpdateNoticeSource
{
    Task<UpdateNotice> ReadUpdateNoticeAsync(CancellationToken token);
}
