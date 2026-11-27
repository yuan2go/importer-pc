using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class WorkstationLanguageBinding : IDisposable
{
    readonly string applicationId;
    readonly ResourceDictionary resources;
    readonly Dispatcher dispatcher;
    readonly Action<string> report;
    readonly WorkstationLocalizationManager language = new();
    readonly FileSystemWatcher? watcher;
    bool disposed;

    public WorkstationLanguageBinding(string applicationId, ResourceDictionary resources, Dispatcher dispatcher,
        Action<string> report, bool watchOtherProcesses = false)
    {
        this.applicationId = applicationId;
        this.resources = resources;
        this.dispatcher = dispatcher;
        this.report = report;
        ReadAndApply();
        PresentationPreferences.LanguageChanged += Saved;
        if (!watchOtherProcesses) return;
        try
        {
            var file = PresentationPreferences.LanguagePreferenceFile(applicationId);
            var directory = Path.GetDirectoryName(file)!;
            // Installation normally wrote the initial file. Creating an empty
            // preference directory also admits the first save on an older station.
            Directory.CreateDirectory(directory);
            watcher = new FileSystemWatcher(directory, Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Renamed += Changed;
            watcher.Deleted += Changed;
            watcher.EnableRaisingEvents = true;
            ReadAndApply();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            report(UiText.Get("Common.LanguageWatchUnavailable") + " " + DiagnosticText.Protect(error.Message));
        }
    }

    void ReadAndApply()
    {
        if (disposed) return;
        string selected;
        try { selected = PresentationPreferences.ReadLanguage(applicationId); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Apply(PresentationPreferences.DefaultLanguage);
            report(UiText.Get("Common.LanguageUnreadable") + " " + DiagnosticText.Protect(error.Message));
            return;
        }
        Apply(selected);
    }

    void Apply(string selected)
    {
        // Initial application also establishes the UI culture if the persisted
        // selection is the implicit Chinese fallback.
        if (!applied || selected != language.CurrentLanguage)
        {
            language.Apply(resources, selected);
            applied = true;
        }
    }

    bool applied;
    void Changed(object sender, FileSystemEventArgs e) =>
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ReadAndApply));

    void Saved(string application, string selected)
    {
        if (application != applicationId) return;
        void Refresh() { if (!disposed) Apply(selected); }
        if (dispatcher.CheckAccess()) Refresh();
        else _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Refresh));
    }

    public void Dispose()
    {
        disposed = true;
        PresentationPreferences.LanguageChanged -= Saved;
        watcher?.Dispose();
    }
}
