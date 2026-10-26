using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string savedEditorSignature = "";
    // Compare raw editable values, including invalid fields and omitted defaults.
    // Building a configuration here would reject malformed drafts or normalize them.
    string ConfigurationEditSignature() => Wire.Encode(new
    {
        Capabilities = CapabilityDrafts.Select(DraftSignature),
        Readers = ReaderDrafts.Select(x => DraftSignature(x.Capability)),
        Roles = ConfigurationRoles.Select(x => new { x.Role, Instance = x.Selection.Capability?.InstanceId }),
        Positions = PositionDrafts.Select(x => new { x.Id, Scale = x.Scale.Capability?.InstanceId, Camera = x.Camera.Capability?.InstanceId }),
        StructuredPolicy
    });
    static object DraftSignature(CapabilityDraft draft) => new
    {
        draft.Kind, draft.Role, draft.Provider, draft.Enabled, draft.Bound, draft.ConfigureLater, draft.InstanceId, draft.Target, draft.Contract,
        Parameters = draft.Parameters.Select(x => new { x.Field.Key, x.Value })
    };
    public bool HasUnsavedConfiguration => configurationAuthority switch
    {
        "未加载" => ConfigurationDraft.Length != 0,
        "高级JSON" => ConfigurationDraft != savedCandidate,
        _ => ConfigurationEditSignature() != savedEditorSignature
    };
    void AcceptEditorBaseline() => savedEditorSignature = ConfigurationEditSignature();
}
