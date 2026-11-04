using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

// Record page task detail: plain words for the station, technical identities only for handover.
// Synthetic operations; no runtime, camera or RCS.
static class HistoryDetailSheet
{
    public static void Run(string workspace)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL DETAIL " + name); Console.WriteLine("PASS DETAIL " + name); }
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var at = new DateTimeOffset(2026, 11, 3, 8, 42, 47, TimeSpan.Zero);
        ParcelOperation Make(ParcelState state, string? slot, StageState stage, StageReply[] replies, string? reason = null)
        {
            var op = new ParcelOperation(Wire.Id(), Wire.Id(), "6901209274190", Wire.Id(), at, Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null, state, slot, null, null, reason, PositionId: "left");
            var request = new FrozenRequest(Wire.Id(), c.ApplicationId, op.Id, op.AdmissionId, "wcs-slot", c.Wcs!, "synthetic-only", op.ConfigurationHash, "synthetic-secret-payload", Wire.Hash("synthetic-secret-payload"), at.AddMilliseconds(552));
            return op with { Wcs = new(request, stage, 1, replies) };
        }
        StageReply Reply(bool? accepted, string? slot) => new(Wire.Id(), "r", "h", true, true, accepted, null, slot, "RCS destination assigned; physical completion is unconfirmed", at.AddMilliseconds(779));

        var ok = Make(ParcelState.SlotResolved, "SIM-CHUTE-02", StageState.Accepted, [Reply(true, "SIM-CHUTE-02")]);
        var sheet = ParcelDetailSheets.Build(ok); var text = sheet.ToText();
        Check(sheet.Headline == "格口已取得 · SIM-CHUTE-02" && sheet.Tone == ParcelPresentationTone.Accepted && sheet.Explanation.Contains("以现场为准"),
            "the headline gives the result and chute; the sentence says landing is confirmed on site, not by RCS");
        Check(sheet.Sections.Select(s => s.Title).SequenceEqual(["包裹", "取格口", "交接编号"]), "facts are grouped: parcel, chute request, handover numbers");
        var request = sheet.Sections.Single(s => s.Title == "取格口").Rows.ToDictionary(r => r.Label, r => r.Value);
        Check(request["结果"] == "已分配格口" && request["发送次数"] == "1 次" && request["分配格口"] == "是 · SIM-CHUTE-02" &&
            request["包裹落格"] == "RCS 不回报，以现场为准" && request["返回时间"].Contains("用时 0.23 秒"), "the chute request reads as result, sends, returned chute, landing and response time");
        Check(text.Contains(ok.Id) && text.Contains(ok.Wcs!.Request.Id) && !text.Contains(ok.AdmissionId) && !text.Contains(ok.Wcs.Request.PayloadHash) &&
            !text.Contains(ok.Wcs.Request.Binding.Contract) && !text.Contains(c.Wcs!.Target) && !text.Contains("physical completion") && !text.Contains("SHA"),
            "job and request numbers stay for handover; admission id, hash, contract, target and protocol evidence do not appear");
        Check(!text.Contains("可以供包") && request["包裹落格"] != "已落格" && !sheet.Headline.Contains("落格"), "an assigned chute is never presented as a landed parcel");

        var unknown = Make(ParcelState.WcsUnknown, null, StageState.Unknown, []);
        var u = ParcelDetailSheets.Build(unknown);
        Check(u.Tone == ParcelPresentationTone.Unknown && u.Explanation.Contains("不会自动重发") && u.Explanation.Contains("暂放一旁") &&
            u.Sections.Single(s => s.Title == "取格口").Rows.Any(r => r.Label == "返回" && r.Attention), "an unconfirmed result says no automatic resend, what to do with the parcel, and flags the missing reply");

        var rejected = Make(ParcelState.WcsRejected, null, StageState.Rejected, [Reply(false, null)]);
        var r = ParcelDetailSheets.Build(rejected);
        Check(r.Tone == ParcelPresentationTone.Rejected && r.Sections.Single(s => s.Title == "取格口").Rows.Single(x => x.Label == "分配格口") is { Value: "否", Attention: true },
            "a refused request is shown as no chute, highlighted");

        OriginalFrameArchive Frame(string state) => new(state, new("left-camera", "e", "mvs.frame.5236", ok.AdmissionId, at), 3072, 2048, "images/original-frames/x.png", at);
        var saved = ParcelDetailSheets.Build(ok with { OriginalFrame = Frame("Saved") }).Sections.Single(s => s.Title == "包裹图片");
        var failed = ParcelDetailSheets.Build(ok with { OriginalFrame = Frame("Failed") }).Sections.Single(s => s.Title == "包裹图片");
        Check(saved.ShowsPicture && saved.Rows.Single().Value == "已保存（3072 × 2048）" && !failed.ShowsPicture && failed.Rows.Single().Attention &&
            !ParcelDetailSheets.Build(ok with { OriginalFrame = Frame("Saved") }).ToText().Contains("images/original-frames"),
            "the picture link appears only for a saved image, without its file path; a failed save is flagged");
        var manual = ok with { OperatorInput = new(Wire.Id(), "Keyboard", "operator", null, at) };
        Check(ParcelDetailSheets.Build(manual).Sections[0].Rows.Single(x => x.Label == "来源").Value == "键盘录入" &&
            ParcelDetailSheets.Build(ok).Sections[0].Rows.Single(x => x.Label == "来源").Value == "相机读码", "the input source is named in station words");

        // Record page summary line: counts in station words, technical counts stay behind "更多筛选与查询信息".
        var all = ParcelViewModel.HistoryMetricsText(new(12, 12, 12, 0, 0, 13));
        var pending = ParcelViewModel.HistoryMetricsText(new(5, 5, 2, 1, 0, 5));
        Check(all == "共 12 件 · 已取得格口 12 · 无待核对" && !all.Contains("序号") && !all.Contains("发送次数"), "a clean query reads as parcels and chutes, without sequence numbers or send counts");
        Check(pending == "共 5 件 · 已取得格口 2 · 格口未确认 1，请核对 · 未取得格口 2", "unconfirmed chutes ask for a check, and parcels without a chute are counted");
        Check(ParcelViewModel.HistoryMetricsText(new(0, 0, 0, 0, 0, 0)) == "本次查询没有包裹", "an empty query says so plainly");
    }
}
