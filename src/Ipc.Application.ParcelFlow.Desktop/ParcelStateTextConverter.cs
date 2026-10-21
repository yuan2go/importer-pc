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
            ParcelState.Prepared => "原阶段待执行", ParcelState.ResolvingWcs => "正在请求格口",
            ParcelState.SlotResolved => "格口已取得", ParcelState.WcsUnknown => "上游结果待核对",
            ParcelState.WcsRejected => "上游明确拒绝", ParcelState.Blocked => "作业阻断",
            ParcelState.UnpackRequired => "需拆包检查", ParcelState.Abandoned => "未发送意图已处置",
            ParcelState.WaitingWeight => "等待有效重量", ParcelState.WeightTimedOut => "称重超时",
            ParcelState.CapturingImage => "正在采集图像", ParcelState.UploadingImage => "正在上传原图",
            ParcelState.ImageUnknown => "图像阶段待核对", ParcelState.ImageRejected => "图像阶段拒绝",
            ParcelState.SubmittingPlc => "控制器请求处理中", ParcelState.PlcAccepted => "控制器已接受",
            ParcelState.PlcUnknown => "控制器结果待核对", ParcelState.PlcRejected => "控制器明确拒绝",
            ParcelState.PhysicalCompleted => "已报告物理完成", _ => state.ToString()
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
