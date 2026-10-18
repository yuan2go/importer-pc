using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;

public partial class MainWindow
{
    long alarmSelectionGeneration;
    async void AlarmSelected(object sender, SelectionChangedEventArgs e)
    {
        var ticket = ++alarmSelectionGeneration;
        selectedAlarm = (sender as DataGrid)?.SelectedItem as Alarm;
        if (selectedAlarm is not Alarm alarm) return;
        RequestIdBox.Text = "";
        if (!BeginHistoricalRead("报警关联请求", out var readActor)) return;
        if (alarm.OwnerId is null || Runtime.Application is null) { model.LastResult = "来源诊断：" + alarm.Source + " / " + alarm.Scope + " / " + alarm.Code; return; }
        try
        {
            var details = await Runtime.Application.DetailsAsync(alarm.OwnerId).WaitAsync(lifetime.Token);
            if (closing || ticket != alarmSelectionGeneration || selectedAlarm?.Id != alarm.Id || !ReadIdentityMatches(readActor)) return;
            RequestIdBox.Text = details.FirstOrDefault(x => x.Request.Status == RequestStatus.Unknown)?.Request.Id ?? (alarm.Scope == "request" ? alarm.OwnerId : "");
            if (alarm.Code.StartsWith("LocalDispositionRequired", StringComparison.Ordinal)) model.LastResult = "保留原记录与实体；待取得预留/实体处置证据，当前版本不提供放行命令。";
            else if (details.Length == 0) model.LastResult = "来源诊断：" + alarm.Source + " / " + alarm.Scope + " / " + alarm.Code;
        }
        catch(OperationCanceledException) when(closing) { }
        catch(Exception ex) { if(!closing && ticket == alarmSelectionGeneration && selectedAlarm?.Id == alarm.Id && ReadIdentityMatches(readActor))model.LastResult="关联请求读取失败："+ex.Message; }
    }
    async Task ShowAlarmDiagnosticAsync(Alarm alarm, long selectionTicket, Actor? actor)
    {
        // Source identity comes from the alarm. No owner is inferred from the active
        // lane, box, barcode or whichever control currently has focus.
        var diagnostic = $"报警来源 {alarm.Source} · 作用域 {alarm.Scope}\n{OperatorText.Reason(alarm.Code)}\n发生 {alarm.RaisedAt:O}\n" +
            (alarm.OwnerId is null ? "此报警没有关联作业或请求身份。请检查此来源及其当前健康/证据；已读不会解除阻断。" :
            $"原关联身份 {alarm.OwnerId}；未取得该对象的请求明细，不能替换为当前作业。请检查来源与原对象记录。");
        ObserveReadIdentity();
        var identityTicket = readIdentityGeneration;
        bool Current()
        {
            ObserveReadIdentity();
            return !closing && selectionTicket == alarmSelectionGeneration && identityTicket == readIdentityGeneration && selectedAlarm?.Id == alarm.Id &&
                Equals(HasIdentity ? snapshot?.State.Actor : null, actor);
        }
        if (!Current()) return;
        await NavigateToViewAsync("diagnostics");
        if (!Current() || currentView != "diagnostics" || !IsDrawerOpen) return;
        var source = snapshot?.State.Devices.FirstOrDefault(x => x.Value.DeviceId == alarm.Source);
        var sourceState = source is { Key: not null } device
            ? $"\n当前来源 {device.Key} / {device.Value.DeviceId} · {OperatorText.Health(device.Value.Health.ToString())}\n代次 {device.Value.Epoch} / 顺序 {device.Value.Sequence} · 最后确认 {device.Value.ConfirmedAt:O} · 有效至 {device.Value.ValidUntil:O}\n当前时效：{device.Value.ValidityReason(DateTimeOffset.UtcNow) ?? "有效"}"
            : alarm.Source == "runtime" ? "\n运行所有者：" + Runtime.RuntimeId + "\n" + model.HealthSummary + "\n" + string.Join("；", Runtime.RuntimeDiagnostics) :
            "\n未取得与该来源身份精确匹配的当前设备健康；原报警来源与证据保持，不能用其它设备替代。";
        AlarmDiagnostic.Text = diagnostic + sourceState; AlarmDiagnostic.BringIntoView();
        model.LastResult = alarm.OwnerId is null ? "已进入报警来源诊断；此报警无关联作业或请求" : "已进入原报警来源诊断；关联身份保持原值";
    }
    async void AlarmDetailClick(object sender, RoutedEventArgs e)
    {
        if (selectedAlarm is not Alarm alarm) { model.LastResult = "请选择需要查看的报警"; return; }
        var selectionTicket = alarmSelectionGeneration;
        var diagnosticActor = HasIdentity ? snapshot?.State.Actor : null;
        if (alarm.OwnerId is null) { await ShowAlarmDiagnosticAsync(alarm, selectionTicket, diagnosticActor); return; }
        if (Runtime.Application is null || !BeginHistoricalRead("报警关联详情", out var readActor)) return;
        var generation = ++detailGeneration;
        try
        {
            var details = await Runtime.Application.DetailsAsync(alarm.OwnerId).WaitAsync(lifetime.Token);
            if (closing || generation != detailGeneration || selectionTicket != alarmSelectionGeneration || selectedAlarm?.Id != alarm.Id || !ReadIdentityMatches(readActor)) return;
            var operation = snapshot?.State.Lanes.Values.FirstOrDefault(x => x.Id == alarm.OwnerId);
            if (details.Length != 0 || operation is not null)
            {
                DetailTree.IsEnabled = true;
                RenderDetails(alarm.OwnerId, operation, details);
                NavigateToHistoryDetail();
            }
            else await ShowAlarmDiagnosticAsync(alarm, selectionTicket, readActor);
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex)
        {
            if (!closing && generation == detailGeneration && selectionTicket == alarmSelectionGeneration && selectedAlarm?.Id == alarm.Id && ReadIdentityMatches(readActor))
                model.LastResult = "原报警关联详情读取失败：" + ex.Message + "；保留原报警与关联身份，请重新查询";
        }
    }
}
