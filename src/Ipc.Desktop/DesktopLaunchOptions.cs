using System.IO;
using Ipc.Platform;

namespace Ipc.Desktop;

// Desktop-only selection: never persisted into business configuration or request identity.
public sealed record DesktopLaunchOptions(BusinessLaunch Business, string Ui)
{
    public static DesktopLaunchOptions Parse(string[] args, string applicationId)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i];
            if (key is not ("--config" or "--data-root" or "--view" or "--layout" or "--ui"))
                throw new ArgumentException("启动参数无效：" + key);
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("启动参数缺值：" + key);
            if (!options.TryAdd(key, args[i + 1])) throw new ArgumentException("启动参数重复：" + key);
        }
        var ui = options.GetValueOrDefault("--ui", "classic");
        if (ui is not ("classic" or "supply-station") || ui == "supply-station" && applicationId != "sportmaster")
            throw new ArgumentException("此业务不支持指定UI：" + ui);
        var installed = DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, applicationId);
        return new(new(
            options.GetValueOrDefault("--config", installed?.ConfigurationDirectory ?? Path.Combine(AppContext.BaseDirectory, "config")),
            options.GetValueOrDefault("--data-root", installed?.DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data")),
            options.GetValueOrDefault("--view"), options.GetValueOrDefault("--layout")), ui);
    }
}
