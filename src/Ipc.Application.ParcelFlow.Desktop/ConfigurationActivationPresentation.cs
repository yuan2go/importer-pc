using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public bool HasConfigurationActivationNotice => Snapshot?.Configuration is { } c &&
        (c.Pending is not null || c.Failure is not null || c.Status == "ActivationFailed");
    public string ConfigurationActivationBannerText => Snapshot?.Configuration is { } c && (c.Failure is not null || c.Status == "ActivationFailed")
        ? "设置未生效，仍使用原设置。请查看生效问题，处理后重新保存。" : ConfigurationActivationOutcome;
    public string ConfigurationActivationOutcome
    {
        get
        {
            if (Snapshot?.Configuration is not { } c) return "尚未读取设置。";
            if (c.Failure is not null || c.Status == "ActivationFailed")
            {
                var reason = c.Failure?.Contains("Application activation barrier is closed", StringComparison.Ordinal) == true
                    ? "仍有未处理完的作业、扫码或设备维护，启动检查未通过。请先核对原作业。" + (c.Effective.ApplicationId == "sportmaster" ? "请到“记录”核对原作业并联系维护人员；内置 RCS 不提供结果查询，不会重发。" : "请维护人员依据原请求核对，不会自动重发。")
                    : "启动检查未通过。" + ChineseInterfaceText.ParameterMessage(c.Failure ?? "") + " 请查看诊断详情并处理故障。";
                var before = c.Effective.Wcs?.Parameters?.GetValueOrDefault("request-timeout-ms");
                var after = c.Pending?.Wcs?.Parameters?.GetValueOrDefault("request-timeout-ms");
                var values = before is not null && after is not null && before != after ? $" 当前取格口超时 {before} 毫秒；待生效值 {after} 毫秒。" : "";
                return "设置未生效，当前仍使用原设置。" + values + " " + reason +
                    (c.Pending is null ? "" : " 已保存的修改保留；处理后返回设置检查并重新保存，再重启软件。不要反复重启或删除数据。");
            }
            if (c.Confirmation?.Required == true) return "设置生效情况尚未确认，请查看诊断并完成启动检查。";
            if (c.Pending is null) return "设置已生效。";
            if (Snapshot.ConfigurationActivationBlock is { } block)
                return "设置已保存，尚未生效。" + ParcelInputReasons.ActionableNotice(block) + "处理后重新打开供包台。";
            return "设置已保存，重启后生效。当前作业仍使用原配置。";
        }
    }
}
