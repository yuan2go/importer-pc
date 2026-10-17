using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.Logging.Abstractions;
namespace Ipc.Assembly;
static class InstalledBusiness
{
    public static BusinessAssembly Create() => new("supply", launch => new SupplyAssembly(launch));
}
sealed class SupplyAssembly(BusinessLaunch launch) : IBusinessRuntime, IBusinessApplication
{
    readonly ProductRuntime runtime = new(new(Path.Combine(launch.ConfigurationDirectory, "product.json"), Path.Combine(launch.ConfigurationDirectory, "station.json"),
        Path.Combine(launch.ConfigurationDirectory, "presentation.json"), launch.DataRoot, View: launch.View, Layout: launch.Layout), NullLogger<ProductRuntime>.Instance);
    public string Id => "supply";
    public string[] Dependencies => [];
    public BusinessPresentation Presentation => new(ReferenceProduct.Definition.Views, ReferenceProduct.Definition.Layouts);
    public IBusinessApplication Application => this;
    public async Task StartAsync(CancellationToken token) { await runtime.StartAsync(token); if (runtime.Diagnostics.Count > 0) throw new InvalidDataException(string.Join("; ", runtime.Diagnostics));
        runtime.StartHeadlessNotifications((cue, cancellation) => { Console.WriteLine(Wire.Encode(new { Notification = cue, Delivery = "AwaitingExplicitAcknowledgement" })); Console.Out.Flush(); return Task.CompletedTask; }); }
    public Task StopAsync(CancellationToken token) => runtime.StopAsync(token);
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
    public async Task<BusinessReceipt> CommandAsync(BusinessCommand command)
    { if (command.Kind == "AcknowledgeNotification")
        {
            if (!long.TryParse(command.Value, out var sequence)) throw new GuardException("Notification sequence required");
            await runtime.AcknowledgeHeadlessNotificationAsync(sequence);
            return new("Persisted", "Notification acknowledged by console receiver", (await runtime.Application!.SnapshotAsync()).State.Revision);
        }
        var result = await runtime.CommandAsync(new(command.Id, command.ExpectedRevision, command.Kind, command.Owner, command.Value)); return new(result.Status, result.Reason, result.Revision); }
    public async Task<BusinessProjection> QueryAsync()
    {
        var state = await runtime.Application!.SnapshotAsync();
        return new(runtime.RuntimeId, state.State.Revision, state.State.Stopping ? "Stopping" : "Running", new() {
            ["Business"] = Id, ["AcceptedQuantity"] = state.AcceptedQuantity.ToString(), ["Session"] = state.State.Session?.Id ?? "",
            ["Admission"] = state.Admit.Allowed.ToString(), ["Blocks"] = string.Join("; ", state.Admit.Reasons) });
    }
}
