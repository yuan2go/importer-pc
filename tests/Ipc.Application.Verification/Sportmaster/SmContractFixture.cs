using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// Current SM contracts, real SQLite/configuration files and the built-in RCS over loopback.
// Faults are injected at owned persistence/publication boundaries, never by replaying a request.
internal static class SmContractFixture
{
    internal static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS CONTRACT " + message); }
    internal static T Field<T>(ParcelRuntime app, string name) => (T)typeof(ParcelRuntime)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    internal static PositionSnapshot Left(ParcelSnapshot snapshot) => snapshot.Positions!.Single(p => p.Id == "left");
    internal static string DirectoryFor(BusinessLaunch launch, ParcelConfiguration config) =>
        Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId);
    internal static async Task<Exception> Failure(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the injected failure to remain visible");
    }
    internal static async Task Pause(ParcelRuntime app)
    {
        if (!(await app.ManagementModeStatusAsync()).Active) await ManagementModeFixture.Authorize(app);
        await CameraRoundHarness.Until(async () =>
        {
            var snapshot = await app.SnapshotAsync();
            var result = await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            if (result.Status == "Conflict") return false;
            Check(result.Status == "Persisted", "backup fixture pauses through the application contract: " + result.Reason);
            return true;
        });
    }
}
