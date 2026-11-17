internal static class VerificationArguments
{
    // The first argument is the workspace; a suite may then have its own
    // documented operands/options. Reject a second suite before any dispatch.
    internal static void Require(string[] args)
    {
        if (args.Length < 2) return;
        var selector = args[1];
        string[] values = selector switch
        {
            "--mvs-latency" => ["--timing-output", "--repeats", "--private-pgm-directory"],
            "--decode-format-benchmark" => ["--rounds"],
            "--decode-tilt-benchmark" => ["--rounds", "--scale", "--images", "--expect"],
            "--mvs-software-flow" => ["--live-ip", "--private-pgm", "--rcs-endpoint", "--rcs-ledger", "--sdk-directory", "--seconds", "--serial-file"],
            "--sm-built-in-only" => ["--installed-configuration-directory", "--installed-configuration-file", "--installed-data-root"],
            "--installer-options" or "--shortcut-lifecycle" => ["--expected-acp"],
            _ => []
        };
        string[] flags = selector switch
        {
            "--mvs-latency" => ["--reference-contrast", "--compare-fast-search"],
            "--mvs-software-flow" => ["--bundled-native", "--isolated-simulation-preflight", "--six-megapixel"],
            "--installer-storage-matrix-direct" => ["--fresh-d-only"],
            "--p0-supervised-child" => ["--sleep", "--normal", "--fail"],
            _ => []
        };
        HashSet<string> seen = [selector];
        for (var i = 2; i < args.Length; i++)
        {
            var option = args[i];
            if (!option.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!seen.Add(option)) throw new ArgumentException("Duplicate verification option or suite: " + option);
            if (values.Contains(option))
            {
                if (++i == args.Length) throw new ArgumentException("Verification option requires a value: " + option);
                continue;
            }
            if (!flags.Contains(option)) throw new ArgumentException("Unsupported option for " + selector + ": " + option);
        }
    }
}
