using System.Reflection;
namespace Ipc.Platform;

// Installed composition registers public contracts before package inspection. The platform
// knows no business assembly names; packages cannot extend the approved set after sealing.
public static class ExtensionPublicContracts
{
    static readonly object gate = new();
    static readonly Dictionary<string, Assembly> contracts = new(StringComparer.OrdinalIgnoreCase)
        { [typeof(IObservationExtension).Assembly.GetName().Name!] = typeof(IObservationExtension).Assembly };
    static bool sealedPolicy;
    public static void Register(Assembly contract)
    {
        lock (gate)
        {
            var name = contract.GetName().Name!;
            if (contracts.TryGetValue(name, out var existing))
            { if (!ReferenceEquals(existing, contract)) throw new InvalidOperationException("Conflicting installed public contract"); return; }
            if (sealedPolicy) throw new InvalidOperationException("Public contract policy is sealed before inspection");
            contracts.Add(name, contract);
        }
    }
    public static IReadOnlyDictionary<string, Assembly> Snapshot()
    { lock (gate) { sealedPolicy = true; return new Dictionary<string, Assembly>(contracts, StringComparer.OrdinalIgnoreCase); } }
}
