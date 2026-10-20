using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

public sealed record BarcodeFilterPolicy(string Pattern);
// Original candidates live in Observation. This evidence is immutable with its input/job.
public sealed record BarcodeFilterResult(string Outcome, string? SelectedCode, int Matches, string? RuleHash, string Detail);
public sealed record ConfigurationSubmissionResult(string Status, string Reason, long Revision, string CandidateHash);
