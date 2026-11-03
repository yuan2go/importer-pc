using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IUpdateNoticeSource
{
    // <data root>\updates\release.json, or a pointer there to a release note on a local share.
    // Read off the business gate: it touches no business state and a slow share cannot hold it.
    public Task<UpdateNotice> ReadUpdateNoticeAsync(CancellationToken token) => Task.Run(() =>
        UpdateNotices.Read(Path.Combine(launch.DataRoot, UpdateNotices.FolderName), "importer-pc-" + Id,
            BusinessCompatibility.VersionOf(definition.GetType().Assembly), TimeSpan.FromSeconds(3)), token);
}
