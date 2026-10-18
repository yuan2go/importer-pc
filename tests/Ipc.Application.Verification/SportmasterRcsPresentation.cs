using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static class SportmasterRcsPresentation
{
    static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL RCS20 presentation: " + message); Console.WriteLine("PASS RCS20 presentation: " + message); }

    internal static async Task Verify(ParcelRuntime app, ParcelOperation assigned, ParcelOperation unpack)
    {
        var view = new ParcelViewModel(app, "Synthetic RCS", "Sportmaster");
        await view.RefreshAsync();
        view.SelectedPositionId = "left";
        Check(view.CurrentStage == "需拆包 · 本位等待检查处置", "unpack has an explicit Chinese operator state without implying counting or submission");
        view.Selected = unpack;
        Check(view.Details.Contains("拆包条码 SYNTH-0001 · SKU SYNTH-SKU · 扫描数量 1", StringComparison.Ordinal) &&
            view.Details.Contains("格口 SYNTH-CHUTE · 订单 SYNTH-ORDER · 剩余 0 · 阶段 ASSIGNED", StringComparison.Ordinal) &&
            view.Details.EndsWith(Wire.Encode(unpack), StringComparison.Ordinal), "unpack fields and zero remaining are visible alongside the complete original snapshot");
        view.Selected = assigned;
        Check(view.Details.Contains("任务 SYNTH-TASK · SKU SYNTH-SKU · 格口 SYNTH-CHUTE", StringComparison.Ordinal) &&
            view.Details.Contains("长度 未取得 · 宽度 未取得 · 高度 未取得 · 重量 未取得", StringComparison.Ordinal) &&
            view.Details.EndsWith(Wire.Encode(assigned), StringComparison.Ordinal), "assigned task, SKU and unknown measurements stay distinct from fabricated zero values");
        var alternate = new SupplyStationViewModel(app);
        await alternate.RefreshAsync();
        Check(alternate.Left.Status=="需拆包 · 本位等待检查处置" && alternate.Left.ResultReason.Contains("剩余 0 · 阶段 ASSIGNED",StringComparison.Ordinal) && alternate.Left.Slot=="未取得",
            "alternate UI keeps NEED_UNPACK explicit with zero remaining and no fabricated destination");
        Check(alternate.Right.ResultReason.Contains("任务 SYNTH-TASK · SKU SYNTH-SKU · 格口 SYNTH-CHUTE",StringComparison.Ordinal) && alternate.Right.ResultReason.Contains("重量 未取得",StringComparison.Ordinal) && alternate.Left.RequestId!=alternate.Right.RequestId,
            "alternate UI reuses complete original RCS projection while keeping both position/request identities separate");
        var destination = unpack.Wcs!.Replies.Single().Destination!;
        var changed = destination with { Unpack = destination.Unpack! with { Chutes = [.. destination.Unpack.Chutes, new("SYNTH-CHUTE-2", "SYNTH-ORDER-2", 2, "FUTURE_PHASE")] } };
        view.Selected = unpack with { Wcs = unpack.Wcs with { Replies = [unpack.Wcs.Replies.Single() with { Destination = changed }] } };
        Check(view.Details.Contains("格口 SYNTH-CHUTE-2 · 订单 SYNTH-ORDER-2 · 剩余 2 · 阶段 FUTURE_PHASE", StringComparison.Ordinal), "all chutes and unknown upstream phase values remain visible without local interpretation");
    }
}
