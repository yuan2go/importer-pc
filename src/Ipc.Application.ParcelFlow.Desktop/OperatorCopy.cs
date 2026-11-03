namespace Ipc.Application.ParcelFlow.Desktop;

// Operator-facing vocabulary for the supply station (OPERATOR-COPY-01).
// Status: a short noun phrase. Next step: an imperative that says what to do with
// the parcel in hand. Reason: one sentence a maintainer can act on. Contract terms
// (admission, original request, receipt, snapshot, responsibility) stay in details.
// Presentation only: never write these strings into contracts, audit or drafts, and
// never branch business logic on them.
public static class OperatorCopy
{
    public const string TimeoutUnconfirmed = "格口结果尚未确认。本件暂勿投递，请取下包裹暂放一旁，查看本次记录并通知维护人员核对；不要反复上件。";
    public const string NotReturned = "未返回";
    public const string Paused = "维护中，暂停接收新包裹。";
    public const string PreviousUnconfirmed = "上一件格口未确认，需先处理。";
    public const string StaleStep = "请勿按旧格口投递，等待状态恢复。";
    public const string RescanStep = "请重新扫描，或输入正确条码。";
    public const string PreviousPendingStep = "上一件仍待处理，请先查看详情。";
    public const string UnconfirmedStep = "勿重复上件：包裹暂放一旁，通知维护人员核对。";
    public const string RejectedStep = "未取得格口：取下包裹，可换下一件，或重新放上本件再试；多次失败请放一旁并通知维护人员。";
    public const string SmConfigurationLoaded = "已读取当前设置。仅修改条码规则可直接生效；连接等设置保存后需重启。";
    public const string ConfigurationLoaded = "已读取当前设置。修改保存后，需重启软件才会生效。";
    public const string RestartToApply = "保存后需重启软件生效";

    // Several guards can describe the same condition (for example pause or a missing
    // RCS). Show each condition once; a reason that only prefixes a longer one is dropped.
    public static string JoinReasons(IEnumerable<string> reasons, string separator)
    {
        static string Core(string value) => value.Trim().TrimEnd('。', '；', ';', '.', ' ');
        var items = reasons.Select(Core).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var kept = items.Where(x => !items.Any(other => other.Length > x.Length && other.StartsWith(x, StringComparison.Ordinal))).ToList();
        return kept.Count == 0 ? "" : string.Join(separator, kept.Select(x => x + "。"));
    }
}
