internal static class UiVerificationArguments
{
    static readonly HashSet<string> Scenarios = new(StringComparer.Ordinal)
    {
        "--audit-fixes-26",
        "--audit-performance",
        "--camera-barcode-current",
        "--camera-chain-reliability-ui",
        "--camera-debug",
        "--camera-filter-preview",
        "--camera-preview-lifecycle",
        "--camera-round-ui",
        "--camera-settings-feedback",
        "--camera-stream-ui",
        "--chute-readability",
        "--clean-install-bootstrap",
        "--comm-native-pages",
        "--commercial-productization-native",
        "--complete-backup-ui",
        "--continuous-pda-input",
        "--debug-batch-actions",
        "--debug-log-live",
        "--debug-no-isolation-31-ui",
        "--dual-camera-diagnostics",
        "--first-preference-save",
        "--hikrobot-sdk-editing",
        "--home-centered-states",
        "--industrial-help",
        "--industrial-maintenance-ui",
        "--installer-first-run-ui",
        "--installer-lifecycle",
        "--installer-review-ui",
        "--internal-pages",
        "--latency-capacity",
        "--lifecycle-maintenance-ui",
        "--lifecycle-ux",
        "--maintenance-progress-ui",
        "--management-center",
        "--management-close-read-race",
        "--management-entry-ordering",
        "--management-mode",
        "--management-password-layout",
        "--manual-rcs-debug-ui",
        "--manual-supply-ui",
        "--operator-corrections-29-ui",
        "--operator-review-ui",
        "--operator-ux-18",
        "--operator-workspace",
        "--p0-crash-hooks",
        "--parameter-experience",
        "--parameter-guidance-startup",
        "--parameter-refactor",
        "--parameter-shell-polish",
        "--parameter-v3",
        "--parameter-v3-only",
        "--parcel-picture-copy",
        "--paused-workstation",
        "--position-input-ui",
        "--presentation-ordering-ui",
        "--product-closure-21",
        "--query-filters",
        "--rcs-no-destination-ui",
        "--reader-feedback-22-ui",
        "--reader-robustness-28-ui",
        "--reliability-logs",
        "--restart-barcode-details",
        "--session-shift-25",
        "--site-ui-refinement",
        "--stability-seed-upgrade",
        "--startup-close",
        "--startup-defaults-ui",
        "--startup-experience",
        "--startup-read-failure",
        "--station-exit-dialog",
        "--station-latency-ui",
        "--station-position-control-ui",
        "--station-responsive-ui",
        "--station-settings",
        "--task-hierarchy",
        "--task-hierarchy-chain",
        "--task-hierarchy-parameters",
        "--task-redesign",
        "--typography",
        "--typography-comparison",
        "--ui-spec-24",
        "--uninstall-result-actions",
        "--unread-package-27-ui",
        "--unsaved-window-actions",
        "--visual-productization",
        "--win11-stability",
        "--window-preference",
        "--workstation-barcode-ready",
        "--workstation-status-logs",
    };
    internal static string[] Normalize(string[] args)
    {
        string? root = null, scenario = null; HashSet<string> seen = [];
        List<string> options = [];
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (root is not null) throw new ArgumentException("Only one workspace root is accepted");
                root = arg; continue;
            }
            if (!seen.Add(arg)) throw new ArgumentException("Repeated verification argument: " + arg);
            options.Add(arg);
            if (Scenarios.Contains(arg))
            {
                if (scenario is not null) throw new ArgumentException("Select exactly one verification scenario");
                scenario = arg;
            }
            else if (arg is "--latency-output" or "--latency-rows" or "--latency-seconds")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing verification option value: " + arg);
                options.Add(args[i]);
            }
            else if (arg is not ("--baseline" or "--interactive" or "--system-family" or "--observe-only" or "--latency-assert")) throw new ArgumentException("Unknown verification argument: " + arg);
        }
        if (seen.Any(x => x.StartsWith("--latency-", StringComparison.Ordinal)) && scenario != "--latency-capacity") throw new ArgumentException("Latency options require the latency scenario");
        if (seen.Contains("--observe-only") && scenario != "--typography-comparison") throw new ArgumentException("Observation mode requires typography comparison");
        if (seen.Contains("--system-family") && scenario != "--typography-comparison") throw new ArgumentException("Font selection requires typography comparison");
        if (scenario is null && options.Count != 0) throw new ArgumentException("Verification modifiers require a scenario");
        return [Path.GetFullPath(root ?? "."), .. options];
    }
}
