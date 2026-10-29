using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

public sealed record BarcodeFilterPolicy(string Pattern,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExclusionPattern = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RejectMultipleCodes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MultiCodeSeparator = null);
public sealed record BarcodeCandidateDecision(int Index, string Outcome, string Reason);
// Original candidates live in Observation. This evidence is immutable with its input/job.
public sealed record BarcodeFilterResult(string Outcome, string? SelectedCode, int Matches, string? RuleHash, string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeCandidateDecision[]? Candidates = null);
public sealed record ConfigurationSubmissionResult(string Status, string Reason, long Revision, string CandidateHash);
