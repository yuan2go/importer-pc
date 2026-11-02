namespace Ipc.Platform;

public sealed record InstallerCommandLine(string Action, string? DataRoot, string? Log, string OperationId)
{
    public static bool IsQuiet(string[] args) => args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("/quiet=", StringComparison.OrdinalIgnoreCase));
    public static InstallerCommandLine Parse(string[] args)
    {
        if (!IsQuiet(args)) throw new ArgumentException("静默调用必须指定/quiet。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string action = "Install"; string? data = null, log = null, operation = null;
        foreach (var item in args)
        {
            var pair = item.Split('=', 2); var name = pair[0];
            if (!seen.Add(name)) throw new ArgumentException("重复的安装参数：" + name);
            switch (name.ToLowerInvariant())
            {
                case "/quiet":
                case "/norestart":
                    if (pair.Length != 1) throw new ArgumentException("开关不能带值。"); break;
                case "/repair":
                case "/uninstall":
                    if (pair.Length != 1 || action != "Install") throw new ArgumentException("安装、修复和卸载互斥。");
                    action = name.Equals("/repair", StringComparison.OrdinalIgnoreCase) ? "Repair" : "Uninstall"; break;
                case "/dataroot": data = Value(pair); break;
                case "/log": log = Value(pair); break;
                case "/operationid": operation = Value(pair); break;
                default: throw new ArgumentException("未知安装参数：" + name);
            }
        }
        if (operation is not null)
        {
            if (!Guid.TryParseExact(operation, "N", out var parsed) || parsed == Guid.Empty) throw new ArgumentException("OperationId须为非空32位GUID。");
            operation = parsed.ToString("N");
        }
        foreach (var path in new[] { data, log }.OfType<string>())
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("安装路径须为本机绝对路径。");
        return new(action, data, log, operation ?? Guid.NewGuid().ToString("N"));
    }
    static string Value(string[] pair) => pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[1]) ? pair[1] : throw new ArgumentException("参数缺少值。");
}
