using Ipc.Supply;
namespace Ipc.Desktop;

public sealed record ScanSubmission(string CommandId, string OwnerId, long InputVersion, string Text);
public sealed class ScanInputDraft
{
    public string? OwnerId { get; private set; }
    public long Version { get; private set; }
    string text = "";
    public string Text { get => text; set { text = value; Version++; } }
    public void SelectOwner(string? owner)
    { if (owner == OwnerId) return; OwnerId = owner; Text = ""; }
    public ScanSubmission Capture(string commandId) => new(commandId, OwnerId ?? throw new InvalidOperationException("No scan owner"), Version, Text.TrimEnd('\r', '\n', '\t'));
    public bool Consume(ScanSubmission sent, string completedCommandId, CommandReceipt? receipt)
    {
        if (receipt?.Status != "Persisted" || completedCommandId != sent.CommandId || receipt.OwnerId != sent.OwnerId || OwnerId != sent.OwnerId || Version != sent.InputVersion) return false;
        Text = ""; return true;
    }
}
