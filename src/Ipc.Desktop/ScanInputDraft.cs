using Ipc.Supply;
namespace Ipc.Desktop;

public sealed record ScanSubmission(string CommandId, string OwnerId, long InputVersion, string Text);
public sealed class ScanInputDraft
{
    public string? OwnerId { get; private set; }
    public long Version { get; private set; }
    ScanSubmission? inFlight;
    public bool IsInFlight => inFlight is not null;
    public bool TryReceive(string value)
    { if (IsInFlight) return false; Text = value; return true; }
    public bool TryBegin(string commandId, out ScanSubmission? submission)
    {
        submission = null;
        if (inFlight is not null || OwnerId is null || Text.TrimEnd('\r', '\n', '\t').Length == 0) return false;
        submission = Capture(commandId); inFlight = submission; return true;
    }
    public void End(ScanSubmission sent) { if (inFlight == sent) inFlight = null; }
    string text = "";
    public string Text { get => text; set { text = value; Version++; } }
    public void SelectOwner(string? owner)
    { if (owner == OwnerId) return; OwnerId = owner; inFlight = null; Text = ""; }
    public ScanSubmission Capture(string commandId) => new(commandId, OwnerId ?? throw new InvalidOperationException("No scan owner"), Version, Text.TrimEnd('\r', '\n', '\t'));
    public bool Consume(ScanSubmission sent, string completedCommandId, CommandReceipt? receipt)
    {
        if (receipt?.Status != "Persisted" || completedCommandId != sent.CommandId || receipt.OwnerId != sent.OwnerId || OwnerId != sent.OwnerId || Version != sent.InputVersion) return false;
        Text = ""; return true;
    }
}
