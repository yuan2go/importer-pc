using System.IO;
using System.Reflection;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Desktop;

public partial class App
{
    readonly CrashDiagnostics crashDiagnostics;
    public App()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unpublished";
        var commit = "unpublished";
        try
        {
            var release = Path.Combine(AppContext.BaseDirectory, "application-release.json");
            if (File.Exists(release) && new FileInfo(release).Length <= 4 * 1024 * 1024)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(release));
                commit = json.RootElement.GetProperty("SourceCommit").GetString() ?? commit;
                version = json.RootElement.GetProperty("BusinessVersion").GetString() ?? version;
            }
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { commit = "release-identity-unavailable"; }
        string root;
        try { root = Path.Combine(WindowsStoragePaths.UserRoot, "diagnostics", "desktop"); }
        catch (Exception) { root = Path.Combine(AppContext.BaseDirectory, "diagnostics-unavailable"); }
        crashDiagnostics = new(root, version, commit);
        DispatcherUnhandledException += (_, e) => { e.Handled = false; crashDiagnostics.Fatal("DispatcherUnhandledException", e.Exception); };
        Exit += (_, _) => crashDiagnostics.Dispose();
    }
}
