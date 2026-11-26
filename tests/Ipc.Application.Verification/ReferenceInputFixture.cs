using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

internal static class ReferenceInputFixture
{
    internal sealed class InputFactory(IParcelCapabilityFactory inner, bool loseReply = false) : IParcelCapabilityFactory
    {
        public Dictionary<string, InputSource> Readers { get; } = [];
        public bool Emit(string device, string kind, string admission, string[] codes, string source) => Readers[device].Emit(kind, admission, codes, null, source);
        public CapabilityCatalog Catalog() => inner.Catalog();
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); foreach (var r in p.Readers!.Values) Readers.Add(r.DeviceId, new(r));
            return p with { Reader = Readers[p.Reader.DeviceId], Readers = Readers.ToDictionary(x => x.Key, x => (IControlledObservationSource)x.Value), Wcs = loseReply ? new LostReply(p.Wcs!) : p.Wcs };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    internal sealed class InputSource(IControlledObservationSource inner) : IControlledObservationSource, ISyntheticObservationInput
    {
        Func<Observation, bool>? receive;
        public string DeviceId => inner.DeviceId;
        public string Epoch => inner.Epoch;
        public string ConfirmationSource => inner.ConfirmationSource;
        public bool Raw(Observation o) => receive!(o);
        public bool EmitPresence(TrayPresence presence) => ((ISyntheticObservationInput)inner).EmitPresence(presence);
        public bool Emit(string kind, string admission, string[] codes, int? quantity, string source) => ((ISyntheticObservationInput)inner).Emit(kind, admission, codes, quantity, source);
        public Task StartControlledAsync(string epoch, Func<Observation, bool> callback, Action<DeviceHealthConfirmation> health, CancellationToken token)
        { receive = callback; return inner.StartControlledAsync(epoch, callback, health, token); }
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class LostReply(IWcsPort inner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
        public Task<CapabilityHealth> HealthAsync(CancellationToken token) => inner.HealthAsync(token);
        public Task ReconnectAsync(CancellationToken token) => inner.ReconnectAsync(token);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token)
        { await inner.ResolveSlotAsync(request, token); throw new IOException("Synthetic lost reply after original peer commit"); }
        public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => inner.QueryAsync(original, token);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
