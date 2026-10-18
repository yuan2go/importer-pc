using Ipc.Supply;

namespace Ipc.Desktop;

// Presentation only. Application authorization, CAS and activation remain authoritative.
internal static class DeploymentPresentation
{
    public static string Revision(ConfigRevision row) => $"修订 {row.Content.Version} · " + (row.Status switch
    {
        "Validated" => "已保存并验证；下一步选择此修订申请激活",
        "PendingActivation" => "待激活；下一步受控退出并重启，成功取得资源后才生效",
        "Effective" => "当前有效；所有窗口共享此运行部署",
        "ActivationFailed" => "激活失败，保留上一有效版本；检查回执并修正后保存新修订",
        "Rejected" => "保存校验未通过；修正草稿后保存新修订",
        "Superseded" => "已由新修订替代；历史身份保留",
        _ => row.Status + "；查看原回执后处理"
    });
    public static string Summary(IEnumerable<ConfigRevision> rows)
    {
        var saved = rows.OrderByDescending(x => x.At).ToArray();
        return saved.Length == 0 ? "尚无已保存修订；本窗编辑仍为草稿" :
            string.Join("\n", saved.Where(x => x.Status is "Effective" or "PendingActivation" or "ActivationFailed").GroupBy(x => x.Status).Select(x => x.First()).Take(3).Select(Revision)
                .Prepend($"已保存 {saved.Length} 份修订；最近：" + Revision(saved[0])).Distinct());
    }
}
