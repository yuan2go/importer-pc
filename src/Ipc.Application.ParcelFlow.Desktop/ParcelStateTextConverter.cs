using System.Globalization;
using System.Windows.Data;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

// Display only; filters and commands retain the original enum/contract values.
public sealed class ParcelStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string { Length: 0 }) return "全部状态";
        if (!Enum.TryParse<ParcelState>(value?.ToString(), out var state)) return value ?? "未取得";
        return state switch
        {
            ParcelState.Prepared => "待处理",
            ParcelState.ResolvingWcs => "正在获取格口",
            ParcelState.SlotResolved => "格口已取得",
            ParcelState.WcsUnknown => "格口未确认",
            ParcelState.WcsRejected => "未分配格口",
            ParcelState.Blocked => "已受阻",
            ParcelState.UnpackRequired => "需拆包检查",
            ParcelState.Abandoned => "未发送 · 已撤销",
            ParcelState.WaitingWeight => "等待有效重量",
            ParcelState.WeightTimedOut => "称重超时",
            ParcelState.CapturingImage => "正在采集图像",
            ParcelState.UploadingImage => "正在上传原图",
            ParcelState.ImageUnknown => "图片上传未确认",
            ParcelState.ImageRejected => "图像阶段拒绝",
            ParcelState.SubmittingPlc => "控制器请求处理中",
            ParcelState.PlcAccepted => "控制器已接受",
            ParcelState.PlcUnknown => "控制器结果未确认",
            ParcelState.PlcRejected => "控制器明确拒绝",
            ParcelState.PhysicalCompleted => "已报告物理完成",
            _ => state.ToString()
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Display only: the tone of an operation, so list rows colour their status the same way as the detail sheet.
public sealed class ParcelToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ParcelOperation op ? ParcelPresentation.Operation(op, true).Tone : ParcelPresentationTone.Neutral;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class RcsReturnedTaskIdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var taskId = (value as ParcelOperation)?.Wcs?.Replies.LastOrDefault(r => r.Destination is not null)?.Destination?.TaskId;
        return string.IsNullOrWhiteSpace(taskId) ? "—" : taskId;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
