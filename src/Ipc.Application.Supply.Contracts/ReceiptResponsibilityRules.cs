namespace Ipc.Supply;

// Q1/Q2 belong to each receipt occurrence, independently of the request terminal state.
// Unknown future statuses remain unresolved until explicitly understood by a compatible reader.
public static class ReceiptResponsibilityRules
{
    public const string AwaitingResponse = "AwaitingResponse";
    public const string RawUnavailable = "RawUnavailable";
    public const string Committed = "Committed";
    public const string NoResponse = "NoResponse";
    public static bool IsFinal(string status) => status is Committed or NoResponse;
    public static bool IsUnresolved(ReceiptResponsibility receipt) => !IsFinal(receipt.Status);
    public static bool IsKnown(string status) => status is AwaitingResponse or RawUnavailable or Committed or NoResponse;

    // Only caller-owned SQL identifiers are accepted; never take one from configuration or UI.
    // Keep readers, retention, exact-package protection and live guards on one terminal definition.
    public static string UnresolvedSql(string column)
    {
        if (string.IsNullOrEmpty(column) || column.Split('.').Any(part => part.Length == 0 ||
            !char.IsAsciiLetter(part[0]) || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new ArgumentException("Trusted SQL column identifier required", nameof(column));
        return $"{column} NOT IN ('{Committed}','{NoResponse}')";
    }
}
