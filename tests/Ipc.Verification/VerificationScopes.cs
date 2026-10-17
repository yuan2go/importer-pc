internal sealed record VerificationScope(string Selector, string Coverage, bool IncludedByDefault);

internal static class VerificationScopes
{
    public const string Default = "--reconciliation";
    public static readonly VerificationScope[] All =
    [
        new("--reconciliation", "H04 + A01 + E07 + E08 + E11 + E12 + AC14 + AC15 + AC/process baseline; architecture, maintainability and health-profile are explicit separate scopes", true),
        new("--hardening", "H04 recovery, original request and overload", true),
        new("--alignment", "H04 + A01 data/composition/runtime", true),
        new("--data", "A01 data/composition/runtime", true),
        new("--baseline", "AC/process crash baseline", true),
        new("--closure", "E07 sources, health, ownership and shutdown", true),
        new("--followup", "E08 permissions/configuration/two-box journeys", true),
        new("--review-followup", "E11 presentation policies + Product composite ownership", true),
        new("--extensions", "E12 controlled packages, install/recovery and actual Product deployment", true),
        new("--architecture", "E13 permanent identity, managed AssemblyRef/dependency closure, typed integration, frozen requests, Stop ownership and selected business assemblies", false),
        new("--conformance", "AC14 A01-A04/R01-R03; per-service health, configuration CAS and failure retention", true),
        new("--operation-closure", "AC15 independent receipt responsibility Q1/Q2, T3 recovery, current result/input and maintenance/history", true),
        new("--maintainability", "AC16 health renewal, UI operation policies, command/ownership and maintenance risks", false),
        new("--health-profile", "Stable dual-source database write/audit/snapshot latency measurement; explicit diagnostic profile", false)
    ];
    public static VerificationScope Select(string selector) => All.SingleOrDefault(x => x.Selector == selector)
        ?? throw new ArgumentException("Unknown verification selector: " + selector + ". Use --help for actual coverage.");
    public static string Help => "Usage: dotnet Ipc.Verification.dll [workspace] [one selector]\nDefault: " + Default +
        "\n" + string.Join("\n", All.Select(x => x.Selector + "  " + x.Coverage + (x.IncludedByDefault ? " [in default]" : " [explicit only]"))) +
        "\n--help prints this list without starting a fixture. Core policies/cross-builds do not attest Windows UI or real SDK/WCS acceptance.";
}
