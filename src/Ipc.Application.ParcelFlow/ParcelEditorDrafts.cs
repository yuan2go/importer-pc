using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelConfigurationDraftApplication
{
    // Reuse the independent editor's CAS/encryption store; this accessor never
    // constructs a second runtime or a port. It does not submit a candidate.
    ParcelStartupMaintenance EditorAccess() => new(launch, definition,
        () => throw new InvalidOperationException("Editor storage must not construct capabilities"),
        () => { if (!CanConfigure) throw new UnauthorizedAccessException("暂停并认证维护后才能持久化设置草稿"); });
    public async Task<string> ConfigurationEditorHashAsync(long expectedRevision)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (configuration?.Current.Revision != expectedRevision || stopped) throw new InvalidOperationException("编辑基线变化；重新读取比较，保留原输入"); return Wire.FileHash(Path.Combine(directory!, "configuration.json")); }
        finally { gate.Release(); }
    }
    public async Task<StartupSavedDraft?> ReadEditorDraftAsync()
    { await gate.WaitAsync().ConfigureAwait(false); try { return await EditorAccess().ReadDraftAsync(); } finally { gate.Release(); } }
    public async Task<StartupSavedDraft> SaveEditorDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor)
    { await gate.WaitAsync().ConfigureAwait(false); try { return await EditorAccess().SaveDraftAsync(expectedConfigurationHash, expectedDraftHash, editor); } finally { gate.Release(); } }
}
