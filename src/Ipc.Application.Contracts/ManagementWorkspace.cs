using Ipc.Platform;

namespace Ipc.Application.Contracts;

// A management window owns a temporary production fence, independent of the
// saved position switches and of the lifetime of its password authorization.
public interface IManagementWorkspace
{
    Task<string?> ReadManagementWorkspaceOwnerAsync(CancellationToken token) => throw new NotSupportedException("当前业务不能核对调试工作区归属。");
    Task<BusinessReceipt> SetManagementWorkspaceAsync(string windowId, bool open, CancellationToken token);
}
