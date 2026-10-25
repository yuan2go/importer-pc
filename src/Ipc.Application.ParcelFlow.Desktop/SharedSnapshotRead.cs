using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// View models treat the shared detached read as immutable. Editable configuration
// and selections remain owned by each window; the Application still clones each API read.
internal static class SharedSnapshotRead
{
    sealed class Owner
    {
        public readonly object Sync = new(); public Task<ParcelSnapshot>? Pending; public ManagementModeStatus? Authority;
        public ConditionalWeakTable<object, object> Consumers = new();
    }
    static readonly ConditionalWeakTable<IParcelApplication, Owner> owners = new();
    public static Task<ParcelSnapshot> Read(IParcelApplication application, ManagementModeStatus authority, object consumer)
    {
        var owner = owners.GetValue(application, _ => new());
        lock (owner.Sync)
        {
            if (owner.Pending is { IsCompleted: false } pending && owner.Authority == authority && !owner.Consumers.TryGetValue(consumer, out _))
            { owner.Consumers.Add(consumer, new()); return pending; }
            // The same window's newer generation must remain independent of its
            // earlier read; only distinct windows may join an in-flight read.
            owner.Consumers = new(); owner.Consumers.Add(consumer, new());
            owner.Authority = authority; return owner.Pending = Task.Run(application.SnapshotAsync);
        }
    }
}
