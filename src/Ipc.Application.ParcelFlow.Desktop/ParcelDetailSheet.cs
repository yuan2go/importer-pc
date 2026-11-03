using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

// The record page's task detail for people at the station: what happened to this parcel, in
// plain words, then the facts grouped by question. Identifiers stay at the bottom for handover;
// targets, contracts, hashes, file paths and protocol evidence only appear in the management
// "详细信息" view.
public sealed record DetailRow(string Label, string Value, bool Attention = false);
public sealed record DetailSection(string Title, DetailRow[] Rows, string? Note = null);
public sealed record ParcelDetailSheet(string Headline, ParcelPresentationTone Tone, string Explanation, DetailSection[] Sections)
{
    // Plain-text form for copying and for the screen reader.
    public string ToText() => Headline + "\n" + Explanation + string.Concat(Sections.Select(s =>
        "\n\n" + s.Title + string.Concat(s.Rows.Select(r => "\n" + r.Label + "：" + r.Value)) + (s.Note is { } note ? "\n" + note : "")));
}

public static class ParcelDetailSheets
{
    static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public static ParcelDetailSheet Build(ParcelOperation op)
    {
        var status = ParcelPresentation.Operation(op, true);
        var headline = status.Text + (op.Slot is { Length: > 0 } slot ? " · " + slot : "");
        var sections = new List<DetailSection> { Parcel(op) };
        foreach (var (name, stage) in new[] { ("取格口", op.Wcs), ("控制器", op.Plc), ("图像采集", op.Capture), ("图像上传", op.Upload) })
            if (stage is not null) sections.Add(Stage(name, stage));
        if (op.OriginalFrame is { } frame) sections.Add(new("包裹图片", [new("原图", frame.State switch
        {
            "Saved" => "已保存（" + frame.Width + " × " + frame.Height + "）",
            "Pending" => "正在保存",
            "Failed" => "保存失败，交维护人员查看日志",
            _ => "保存中断，交维护人员检查原图",
        }, frame.State is not ("Saved" or "Pending"))]));
        sections.Add(new("交接编号", [new("作业号", op.Id), .. op.Wcs is { } wcs ? [new DetailRow("取格口请求号", wcs.Request.Id)] : Array.Empty<DetailRow>()],
            "向维护人员或 RCS 方核对时提供这些编号。"));
        return new(headline, status.Tone, Explanation(op, status.Tone), [.. sections]);
    }

    static string Explanation(ParcelOperation op, ParcelPresentationTone tone)
    {
        if (op.DebugRequest is not null) return "这是调试页手动发送的一次请求，不计入正常供包。";
        if (ParcelPresentation.RcsNotDelivered(op)) return "请求没有送达 RCS，RCS 没有收到这件包裹。请先检查 RCS 连接；软件不会自动重发。";
        return tone switch
        {
            ParcelPresentationTone.Accepted => op.State == ParcelState.SlotResolved
                ? "RCS 已分配格口。包裹是否已落格以现场为准，RCS 不回报落格。"
                : "处理已完成。",
            ParcelPresentationTone.Unknown => "请求已发出，但没有收到确定的结果。软件不会自动重发；请把包裹暂放一旁，交维护人员按交接编号核对。",
            ParcelPresentationTone.Rejected => "没有取得格口" + (op.Reason is { Length: > 0 } reason ? "：" + ChineseInterfaceText.Notice(reason) : "。") + " 请按提示取下包裹。",
            _ => "正在处理，结果出来后这里会更新。",
        };
    }

    static DetailSection Parcel(ParcelOperation op)
    {
        var source = op.DebugRequest is not null ? "调试页手动发送" :
            op.OperatorInput is { } manual ? manual.Kind == "PdaKeyboard" ? "PDA 录入" : "键盘录入" : "相机读码";
        return new("包裹", [new("条码", op.ParcelCode), new("投递位", ParcelViewModel.PositionName(op.PositionId ?? "main")),
            new("来源", source), new("扫描时间", Time(op.ObservedAt))]);
    }

    static DetailSection Stage(string name, StageFact stage)
    {
        var reply = stage.Replies.LastOrDefault();
        var rows = new List<DetailRow>
        {
            new("结果", stage.State switch
            {
                StageState.Accepted => name == "取格口" ? "已分配格口" : "已完成",
                StageState.Rejected => name == "取格口" ? "未分配格口" : "被拒绝",
                StageState.Unknown => "结果未确认",
                StageState.Conflict => "结果有冲突，交维护人员核对",
                StageState.Cancelled => "已撤销，未发送",
                StageState.Dispatching => "发送中",
                _ => "等待发送",
            }, stage.State is StageState.Unknown or StageState.Conflict or StageState.Rejected),
            new("发送次数", stage.Attempts + " 次"),
            new("请求时间", Time(stage.Request.PreparedAt)),
        };
        if (reply is not null)
        {
            var seconds = (reply.At - stage.Request.PreparedAt).TotalSeconds;
            rows.Add(new("返回时间", Time(reply.At) + (seconds >= 0 ? "（用时 " + seconds.ToString("0.00") + " 秒）" : "")));
            rows.Add(new("送达 RCS", reply.TransportSucceeded ? "是" : "否", !reply.TransportSucceeded));
            rows.Add(new("RCS 确认收到", Known(reply.ProtocolAcknowledged), reply.ProtocolAcknowledged != true));
            rows.Add(new(name == "取格口" ? "分配格口" : "处理结果", reply.BusinessAccepted switch { true => reply.Slot is { Length: > 0 } slot ? "是 · " + slot : "是", false => "否", null => "未回报" }, reply.BusinessAccepted != true));
            rows.Add(new("包裹落格", reply.PhysicalCompleted switch { true => "已落格", false => "未落格", null => "RCS 不回报，以现场为准" }, reply.PhysicalCompleted == false));
        }
        else rows.Add(new("返回", stage.State is StageState.Prepared or StageState.Dispatching ? "等待返回" : "没有收到返回", stage.State is StageState.Unknown));
        return new(name, [.. rows]);
    }

    static string Known(bool? value) => value switch { true => "是", false => "否", null => "未回报" };
}
