using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Desktop;
using Ipc.Platform;
using Ipc.Reference.Protocol;

internal static class SnapshotReadFixture
{
    internal sealed class SnapshotProbe(IParcelApplication inner) : IParcelApplication, IManagementModeFixtureForwarder, ICurrentHistoryFixtureForwarder
    {
        public IParcelApplication InnerAdministration => inner;
        public IParcelHistorySessionApplication HistoryService => (IParcelHistorySessionApplication)inner;
        Func<Task<ParcelSnapshot>>? read;
        TaskCompletionSource readEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task<ParcelSnapshot>>? Read { get => read; set { read = value; readEntered = new(TaskCreationOptions.RunContinuationsAsynchronously); } }
        public Task ReadEntered => readEntered.Task;
        public int Writes;
        public List<BusinessCommand> Commands { get; } = [];
        public List<string> Queries { get; } = [];
        public Task<ParcelSnapshot> SnapshotAsync()
        { var invocation = read; readEntered.TrySetResult(); return invocation?.Invoke() ?? inner.SnapshotAsync(); }
        public Task<ParcelHistoryPage> HistoryAsync(long beforeSequence = long.MaxValue, int limit = 50) => inner.HistoryAsync(beforeSequence, limit);
        public Task<ParcelOperation?> OperationAsync(string id) => inner.OperationAsync(id);
        public Task<BusinessReceipt> CommandAsync(BusinessCommand command) { Writes++; Commands.Add(command); return inner.CommandAsync(command); }
        public Task<BusinessProjection> QueryAsync() => inner.QueryAsync();
        public Task<BusinessReceipt> LoginAsync(string subject, string proof) { Writes++; return inner.LoginAsync(subject, proof); }
        public Task<BusinessReceipt> LogoutAsync() { Writes++; return inner.LogoutAsync(); }
        public Task<BusinessReceipt> AuthorizeMaintenanceAsync(string proof) { Writes++; return inner.AuthorizeMaintenanceAsync(proof); }
        public Task<CapabilityCatalog> CapabilitiesAsync() => inner.CapabilitiesAsync();
        public Task<BusinessReceipt> ManageCapabilityAsync(string action, string identity) { Writes++; return inner.ManageCapabilityAsync(action, identity); }
        public Task<BusinessReceipt> ValidateConfigurationAsync(ParcelConfiguration candidate) => inner.ValidateConfigurationAsync(candidate);
        public Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256) { Writes++; return inner.InstallCapabilityAsync(file, sha256); }
        public Task<BusinessReceipt> StageConfigurationAsync(long revision, ParcelConfiguration candidate, string? reason = null) { Writes++; return inner.StageConfigurationAsync(revision, candidate, reason); }
        public Task<BusinessReceipt> ReconcileAsync(string operationId, string? stage = null) { Writes++; Queries.Add(operationId); return inner.ReconcileAsync(operationId, stage); }
        public Task<BusinessReceipt> ReconnectAsync() { Writes++; return inner.ReconnectAsync(); }
        public Task<BusinessReceipt> BackupAsync(string destination) { Writes++; return inner.BackupAsync(destination); }
        public bool EmitReference(string admission, string code, string source) { Writes++; return inner.EmitReference(admission, code, source); }
        public bool EmitReferenceAt(string position, string admission, string code, string source) { Writes++; return inner.EmitReferenceAt(position, admission, code, source); }
        public bool EmitReferenceWeight(WeightMeasurement measurement) { Writes++; return inner.EmitReferenceWeight(measurement); }
        public Task<BusinessReceipt> ReconnectDeviceAsync(string device) { Writes++; return inner.ReconnectDeviceAsync(device); }
    }
}
