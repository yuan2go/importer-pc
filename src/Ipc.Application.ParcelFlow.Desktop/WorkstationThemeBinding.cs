using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class WorkstationThemeBinding : IDisposable
{
    readonly string applicationId;
    readonly BusinessDesktopProfile profile;
    readonly ResourceDictionary resources;
    readonly Dispatcher dispatcher;
    readonly Action<string> report;
    readonly WorkstationThemeManager manager = new();
    bool disposed;

    public WorkstationThemeBinding(string applicationId, ResourceDictionary resources, Dispatcher dispatcher, Action<string> report)
    {
        this.applicationId = applicationId;
        this.resources = resources;
        this.dispatcher = dispatcher;
        this.report = report;
        profile = BusinessDesktopCatalog.For(applicationId);

        string themeId;
        try { themeId = PresentationPreferences.ReadTheme(applicationId, profile.DefaultThemeId); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            themeId = profile.DefaultThemeId;
            report("本机界面主题未能读取，原文件保留。已使用默认主题。 " + DiagnosticText.Protect(error.Message));
        }
        Apply(themeId);
        PresentationPreferences.ThemeChanged += Saved;
    }

    void Apply(string themeId)
    {
        manager.Apply(resources, themeId, profile.SupportedThemes, profile.DefaultThemeId);
        if (manager.CurrentThemeId != themeId)
            report("已保存的界面主题不可用，当前使用默认主题。");
    }

    void Saved(string application, string themeId)
    {
        if (application != applicationId) return;
        void Refresh() { if (!disposed) Apply(themeId); }
        if (dispatcher.CheckAccess()) Refresh();
        else _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Refresh));
    }

    public void Dispose()
    {
        disposed = true;
        PresentationPreferences.ThemeChanged -= Saved;
    }
}
