using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Only explicitly configured excluded values are background. Package QR codes
// and linear codes follow the same full-string business rules.
public static class CameraBarcodeCandidates
{
    public static int[]? Excluded(BarcodeFilterPolicy? policy, string[] codes, string? issue = null)
    {
        if (policy?.ExclusionPattern is null || issue is not null || BarcodeFiltering.Validate(policy) is not null) return null;
        var selection = BarcodeFiltering.Select(new("", policy.ExclusionPattern), codes, issue);
        if (selection.Candidates is not { } decisions || decisions.Length != codes.Length ||
            selection.Outcome is "IncompleteCandidates" or "FilterTimeout" or "InvalidPattern") return null;
        return decisions.Where(d => d.Outcome == "Excluded").Select(d => d.Index).ToArray();
    }

    public static Observation WithoutBackground(BarcodeFilterPolicy? policy, Observation observation)
    {
        if (observation.OperatorInput is not null || Excluded(policy, observation.Codes, observation.CandidateIssue) is not { Length: > 0 } excluded)
            return observation;
        var remaining = Enumerable.Range(0, observation.Codes.Length).Except(excluded).ToArray();
        return observation with
        {
            Codes = remaining.Select(i => observation.Codes[i]).ToArray(),
            Symbols = observation.Symbols is { } symbols && symbols.Length == observation.Codes.Length
                ? remaining.Select(i => symbols[i]).ToArray() : null
        };
    }
}
