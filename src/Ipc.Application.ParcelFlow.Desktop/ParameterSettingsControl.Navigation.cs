using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    ObjectPage? currentPage;
    bool selectingPage, navigating;
    long navigationGeneration, editRevision;
    ObjectPage[] OrderedPages() => FirstConfiguration ? pages.OrderBy(p => p.Order).ToArray() : pages.ToArray();
    public string CurrentPageTitle => currentPage?.Title ?? "";
    public bool IsReviewPage => currentPage?.Category == 4;
    public bool HasPreviousPage => currentPage is not null && Array.IndexOf(OrderedPages(), currentPage) > 0;
    public string? NextPageTitle
    {
        get { var ordered = OrderedPages(); var index = currentPage is null ? -1 : Array.IndexOf(ordered, currentPage); return index >= 0 && index + 1 < ordered.Length ? ordered[index + 1].Title : null; }
    }
    public bool IsNavigating => navigating;
    public event EventHandler? PageValidationFailed;
    void SelectPageCore(ObjectPage? page)
    {
        if (page is null) return;
        selectingPage = true;
        try { directory.SelectedItem = page; currentPage = page; lastPages[page.Category] = page.Title; pageHost.Content = page.Scroll; directory.ScrollIntoView(page); }
        finally { selectingPage = false; }
        SelectedPageChanged?.Invoke(this, EventArgs.Empty);
    }
    async void DirectorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingPage || directory.SelectedItem is not ObjectPage destination) return;
        if (currentPage is null) { SelectPageCore(destination); return; }
        selectingPage = true;
        try { directory.SelectedItem = currentPage; }
        finally { selectingPage = false; }
        await RequestPageAsync(destination);
    }
    public void MovePrevious()
    {
        if (navigating) return;
        var ordered = OrderedPages(); var index = currentPage is null ? -1 : Array.IndexOf(ordered, currentPage);
        if (index > 0) { search.Clear(); SelectPageCore(ordered[index - 1]); }
    }
    public Task<bool> MoveNextAsync()
    {
        var ordered = OrderedPages(); var index = currentPage is null ? -1 : Array.IndexOf(ordered, currentPage);
        return index >= 0 && index + 1 < ordered.Length ? RequestPageAsync(ordered[index + 1]) : Task.FromResult(false);
    }
    async Task<bool> RequestPageAsync(ObjectPage destination)
    {
        if (navigating || Model is not { } model) return false;
        var ordered = OrderedPages(); var start = currentPage is null ? 0 : Array.IndexOf(ordered, currentPage); var end = Array.IndexOf(ordered, destination);
        if (end < 0) return false;
        if (end <= start) { search.Clear(); SelectPageCore(destination); return true; }
        var generation = navigationGeneration;
        navigating = true; directory.IsEnabled = false; SelectedPageChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            for (var index = Math.Max(0, start); index < end; index++)
            {
                var page = ordered[index]; SelectPageCore(page);
                if (page.Category == 4) continue;
                var revision = editRevision;
                var fields = locations.Where(x => ReferenceEquals(x.Value.Page, page)).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
                var valid = await model.ValidatePageAsync(fields);
                if (generation != navigationGeneration || !ReferenceEquals(model, Model)) return false;
                if (revision != editRevision) { page.Summary.Text = "输入已改变，请重新检查当前页。"; page.Summary.Visibility = Visibility.Visible; PageValidationFailed?.Invoke(this, EventArgs.Empty); return false; }
                if (fields.Contains("DisplayNameDraft") && RuntimeContext is { } runtime)
                {
                    var path = "TargetEfficiencyDraft";
                    try
                    {
                        int? target = runtime.TargetEfficiencyDraft.Length == 0 ? null : ConfigurationPolicyDraft.Integer(runtime.TargetEfficiencyDraft, "目标效率", 1, 100000);
                        path = "DisplayNameDraft";
                        Ipc.Platform.PresentationPreferences.ValidateStation(new(1, runtime.DisplayNameDraft, target));
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.IO.InvalidDataException)
                    { model.ReportField("系统", path, error.Message); valid = false; }
                }
                if (!valid)
                {
                    PageValidationFailed?.Invoke(this, EventArgs.Empty);
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { if (generation == navigationGeneration && ReferenceEquals(currentPage, page)) FocusFirstIssue(); }));
                    return false;
                }
            }
            search.Clear(); SelectPageCore(destination); return true;
        }
        catch (Exception error)
        {
            if (generation == navigationGeneration && ReferenceEquals(model, Model))
            {
                model.Report(Ipc.Platform.DiagnosticText.Protect(error.Message));
                if (currentPage is { } page)
                {
                    page.Summary.Text = error is OperationCanceledException ? "检查已取消，输入保留。" : "当前页检查未完成，请查看诊断详情后重试。";
                    page.Summary.Visibility = Visibility.Visible;
                    page.Diagnostic.Text += "\n" + Ipc.Platform.DiagnosticText.Protect(error.ToString());
                }
                PageValidationFailed?.Invoke(this, EventArgs.Empty);
            }
            return false;
        }
        finally { navigating = false; directory.IsEnabled = true; SelectedPageChanged?.Invoke(this, EventArgs.Empty); }
    }
    async Task NavigateSearchAsync(string path)
    {
        if (locations.TryGetValue(path, out var location) && location.Applicable?.Invoke() != false && await RequestPageAsync(location.Page)) NavigateTo(path);
    }
}
