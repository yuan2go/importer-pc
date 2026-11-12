using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

sealed class BackupSetsWindow : StartupWorkflowWindow
{
    readonly IStartupMaintenance maintenance;
    readonly TextBox directory, deliveryDestination;
    readonly DataGrid sets = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 120, MaxHeight = 300, RowHeight = 44 };
    readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 70, MaxHeight = 160 };
    readonly Button export, delete, select;
    string? pendingDeletion;
    (string Backup, string Identity, string Target)? pendingExport;
    public string? SelectedBackup { get; private set; }
    public BackupSetsWindow(IStartupMaintenance maintenance, string initialDirectory, bool selectForRestore = false) : base("完整备份 · 核验与保管")
    {
        this.maintenance = maintenance; Width = 880; Height = 690;
        Note("一份完整备份包含数据库与配套恢复内容。交付会复制并核验整份备份；原集保留。残缺集和孤立数据库不能用于恢复。删除只处理所选原清单，未知文件保留。");
        directory = Field("完整备份所在文件夹", initialDirectory, _ => { });
        var chooseDirectory = new Button { Content = "选择文件夹…", MinHeight = 44, Margin = new(8, 0, 0, 0) };
        chooseDirectory.Click += (_, _) => { var picker = new OpenFolderDialog { Title = "选择完整备份所在文件夹" }; if (picker.ShowDialog(this) == true) directory.Text = picker.FolderName; };
        Body.Children.Remove(directory); var sourceRow = new DockPanel(); DockPanel.SetDock(chooseDirectory, Dock.Right); sourceRow.Children.Add(chooseDirectory); sourceRow.Children.Add(directory); Body.Children.Add(sourceRow);
        deliveryDestination = Field("交付文件夹（新建；续办使用原目标）", "", _ => { });
        var chooseParent = new Button { Content = "选择父文件夹…", MinHeight = 44, Margin = new(8, 0, 0, 0) };
        chooseParent.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "选择交付位置的父文件夹" };
            if (picker.ShowDialog(this) == true) deliveryDestination.Text = Path.Combine(picker.FolderName, Path.GetFileNameWithoutExtension((sets.SelectedItem as StartupBackupItem)?.Path ?? "备份") + "-完整备份");
        };
        Body.Children.Remove(deliveryDestination); var targetRow = new DockPanel(); DockPanel.SetDock(chooseParent, Dock.Right); targetRow.Children.Add(chooseParent); targetRow.Children.Add(deliveryDestination); Body.Children.Add(targetRow);
        foreach (var (header, property) in new[] { ("完整性", "StatusText"), ("时间", "At"), ("字节", "Bytes"), ("备份", "Path") })
            sets.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(property), Width = property == "Path" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        Body.Children.Add(sets); Body.Children.Add(detail);
        sets.SelectionChanged += (_, _) => { detail.Text = (sets.SelectedItem as StartupBackupItem)?.Summary ?? ""; UpdateActions(); };
        var read = Action("读取并核验", Read, readOnly: true);
        export = Action("交付完整备份…", async () =>
        {
            var selected = Selected("Verified");
            if (string.IsNullOrWhiteSpace(deliveryDestination.Text)) throw new InvalidOperationException("先选择新的交付文件夹；原次中断则填写原目标。");
            var target = deliveryDestination.Text;
            if (UnknownOutcome)
            {
                if (pendingExport is not { } pending || !pending.Backup.Equals(selected.Path, StringComparison.OrdinalIgnoreCase) || pending.Identity != selected.Identity || !pending.Target.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new InstallationOutcomeUnknownException("先按原完整集和原目标核对上次交付。");
                var original = await Task.Run(() => maintenance.InspectBackupDeliveryAsync(selected.Path, selected.Identity!, target));
                ResolveKnownMaintenanceOutcome(); pendingExport = null; HideCompletedMaintenanceProgress();
                Message(original.Detail); detail.Text = original.Detail + "\n" + original.Path; return;
            }
            pendingExport = (selected.Path, selected.Identity!, target);
            var result = await RunMaintenance("复制整份备份并核验；原集保留。原目标：" + target, (p, t) => maintenance.ExportBackupSetAsync(selected.Path, selected.Identity!, target, p, t));
            pendingExport = null; HideCompletedMaintenanceProgress();
            detail.Text = result.Detail + "\n交付后的恢复选择：" + result.Path + "\n完整身份：" + result.Identity; Message(result.Detail);
        }, readOnly: true);
        delete = Action("删除所选备份…", async () =>
        {
            var selected = Selected("Verified", "Deleting", "OrphanDatabase", "InvalidOrIncomplete");
            if (UnknownOutcome)
            {
                if (!string.Equals(pendingDeletion, selected.Path, StringComparison.OrdinalIgnoreCase) || selected.Status != "Deleting") throw new InstallationOutcomeUnknownException("先读取原删除结果，不处理另一份备份。");
                ResolveKnownMaintenanceOutcome();
            }
            if (MessageBox.Show(this, "仅删除这份备份的数据库和原配套清单，不可撤销；活动恢复引用、未知内容和占用余项保留。\n" + selected.Path,
                "删除所选完整备份", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            pendingDeletion = selected.Path;
            var result = await RunMaintenance("删除所选完整备份，冻结范围并保留原删除回执。", (p, t) => maintenance.DeleteBackupSetAsync(selected.Path, selected.Identity!, p, t));
            pendingDeletion = null; await Read(); detail.Text = result.Detail + "\n" + string.Join("\n", result.Remaining); Message(result.Detail);
        }, readOnly: true);
        select = Action("选择此备份恢复", () => { SelectedBackup = Selected("Verified").Path; Busy = false; DialogResult = true; return Task.CompletedTask; }, readOnly: true);
        select.Visibility = selectForRestore ? Visibility.Visible : Visibility.Collapsed;
        var close = Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true); close.IsCancel = true;
        foreach (var (button, id) in new[] { (read, "Read"), (export, "Export"), (delete, "Delete"), (select, "SelectRestore"), (close, "Close") }) AutomationProperties.SetAutomationId(button, "BackupSet." + id);
        Loaded += async (_, _) => { if (Directory.Exists(initialDirectory) && Path.GetPathRoot(initialDirectory) != initialDirectory) await Run(Read); };
        UpdateActions();
    }
    StartupBackupItem Selected(params string[] allowed)
    {
        if (sets.SelectedItem is not StartupBackupItem item || item.Identity is null || !allowed.Contains(item.Status)) throw new InvalidOperationException("先读取并选择可办理的完整备份；残缺或未知范围保留。");
        return item;
    }
    async Task Read()
    {
        var selected = (sets.SelectedItem as StartupBackupItem)?.Path;
        var selectedDirectory = directory.Text;
        var items = await RunMaintenance("只读核验完整备份及原删除结果，不复制或删除。", (p, t) => maintenance.ListBackupsAsync(selectedDirectory, p, t), readOnly: true);
        sets.ItemsSource = items; sets.SelectedItem = items.SingleOrDefault(i => i.Path == selected);
        if (!UnknownOutcome) HideCompletedMaintenanceProgress();
        Message("备份列表核验完成；选择备份后查看完整性和下一步。未完成的删除沿原清单继续。");
    }
    void UpdateActions()
    {
        if (export is null) return;
        var item = sets.SelectedItem as StartupBackupItem;
        export.IsEnabled = item?.Status == "Verified" && (!UnknownOutcome || pendingExport?.Backup == item.Path);
        select.IsEnabled = item?.Status == "Verified" && !UnknownOutcome;
        delete.IsEnabled = item?.CanDelete == true;
    }
    protected override void OperationCompleted() => UpdateActions();
}
