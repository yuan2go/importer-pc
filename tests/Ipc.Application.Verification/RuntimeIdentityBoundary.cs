using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Platform;

static class RuntimeIdentityBoundary
{
    public static async Task Run(string root)
    {
        foreach (var flow in new FlowDefinition[] { new SportmasterFlow(), new DirectPlcFlow(), new WcsToPlcFlow() })
        {
            var directory = Path.Combine(root, flow.Id); Directory.CreateDirectory(directory);
            // Deliberately invalid configuration: identity denial must win before even parsing it.
            var original = Path.Combine(directory, "business.json"); File.WriteAllText(original, "original unsupported input");
            var before = File.ReadAllBytes(original);
            var data = Path.Combine(directory, "data");
            await using var runtime = new ParcelRuntime(new(directory, data, null, null), flow, new UnreachableFactory());
            if (runtime.RuntimeIdentityPreflight != (Action)MachineRuntimePolicy.RequireRuntimeIdentity)
                throw new Exception("Production identity validator was replaced");
            var denial = new UnauthorizedAccessException("Synthetic denied Windows identity");
            runtime.RuntimeIdentityPreflight = () => throw denial;
            try { await runtime.StartAsync(default); throw new Exception("Denied identity started a business"); }
            catch (UnauthorizedAccessException error) when (ReferenceEquals(error, denial)) { }
            await runtime.DisposeAsync();
            if (Directory.Exists(data) || !File.ReadAllBytes(original).SequenceEqual(before) || Directory.GetFileSystemEntries(directory).Length != 1)
                throw new Exception("Identity denial changed original input or created runtime state");
            Console.WriteLine("PASS IDENTITY-BOUNDARY " + flow.Id + ": original input unchanged, no database, lease, normalization or device access");
        }
    }

    sealed class UnreachableFactory : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => throw new Exception("Denied identity reached normalization");
        public void Validate(ParcelConfiguration c) => throw new Exception("Denied identity reached capability validation");
        public ParcelPorts Create(ParcelConfiguration c) => throw new Exception("Denied identity constructed resources");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
