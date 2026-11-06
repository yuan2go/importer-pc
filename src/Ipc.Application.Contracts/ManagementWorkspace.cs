using Ipc.Platform;

namespace Ipc.Application.Contracts;

// A management window owns a temporary production fence, independent of the
// saved position switches and of the lifetime of its password authorization.
public interface IManagementWorkspace
{
    Task<BusinessReceipt> SetManagementWorkspaceAsync(string windowId, bool open, CancellationToken token);
}
