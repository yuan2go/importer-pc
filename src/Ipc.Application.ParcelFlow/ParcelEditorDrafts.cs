using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelConfigurationDraftApplication
{
    // Reuse the independent editor's CAS/encryption store; this accessor never
    // constructs a second runtime or a port. It does not submit a candidate.
    ParcelStartupMaintenance EditorAccess(bool saving = false) => new(launch, definition,
        () => throw new InvalidOperationException("Editor storage must not construct capabilities"),
        () =>
        {
            Administrator.Require();
            if (!saving) return;
            if (repository?.Paused != true) throw new UnauthorizedAccessException("先暂停新入场，再保存未完成设置；当前输入保留");
            if (!MaintenanceAuthorized) throw new UnauthorizedAccessException("维护认证缺失或已过期，重新认证后保存；当前输入保留");
            if (externalWaits != 0) throw new InvalidOperationException("在途检查尚未排空，稍后重新保存未完成设置；当前输入保留，不重复发送业务请求");
        });
    public async Task<string> ConfigurationEditorHashAsync(long expectedRevision)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { Administrator.Require(); if (configuration?.Current.Revision != expectedRevision || stopped) throw new InvalidOperationException("编辑基线变化；重新读取比较，保留原输入"); return Wire.FileHash(Path.Combine(directory!, "configuration.json")); }
        finally { gate.Release(); }
    }
    public async Task<StartupSavedDraft?> ReadEditorDraftAsync()
    { await gate.WaitAsync().ConfigureAwait(false); try { Administrator.Require(); return await EditorAccess().ReadDraftAsync(); } finally { gate.Release(); } }
    public async Task<StartupSavedDraft> SaveEditorDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor)
    { await gate.WaitAsync().ConfigureAwait(false); try { Administrator.Require(); return await EditorAccess(true).SaveDraftAsync(expectedConfigurationHash, expectedDraftHash, editor); } finally { gate.Release(); } }
}
