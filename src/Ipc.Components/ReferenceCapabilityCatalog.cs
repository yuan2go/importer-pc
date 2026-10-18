using Ipc.Platform;
namespace Ipc.Components;

// Owned by reference components, not by an Application or UI. A real provider supplies
// its own signed schema and requirements; these fields are only the reference protocol.
public static class ReferenceCapabilityCatalog
{
    static ParameterField Choice(string key, string[] values, string value) => new(key, "参考协议", key, "choice", Default: value, Choices: values);
    static readonly ParameterField Decision = Choice("decision", ["accept", "reject"], "accept");
    static readonly ParameterField Reply = Choice("reply-mode", ["normal", "lose-after-commit"], "normal");
    static readonly ParameterField Peer = new("peer-root", "参考协议", "持久接收账目录", "string", MaximumLength: 2048);
    static readonly ParameterField Endpoint = new("endpoint", "参考协议", "独立loopback服务地址", "string", MaximumLength: 256);
    static CapabilityDescriptor Provider(string kind, ParameterField[] fields, bool query = false, bool physical = false) =>
        new(kind, "reference-" + kind, "1.0.0", "PARCEL-REFERENCE-v1", fields, new(Query: query, PhysicalConfirmation: physical));
    public static CapabilityDescriptor[] Providers => [
        Provider("weight", []),
        Provider("login", [new("subjects", "合成身份", "允许主体（逗号分隔）", "string", true, "alice,bob", MaximumLength: 1024), Choice("reply-mode", ["normal", "unavailable"], "normal")]),
        Provider("camera", [Decision, Reply], true),
        Provider("upload", [Decision, Reply, Peer, Endpoint], true),
        Provider("integration", [Decision, Reply, Peer, Endpoint, new("slot", "参考协议", "参考格口", "string", Default: "REFERENCE-07", MaximumLength: 64)], true),
        Provider("plc", [Decision, Reply, Peer, Endpoint, Choice("complete-on-query", ["true", "false"], "false")], true, true)
    ];
}
