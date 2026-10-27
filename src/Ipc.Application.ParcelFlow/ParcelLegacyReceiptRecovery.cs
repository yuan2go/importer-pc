using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Refusal boundary for retired callers. Current-format original Query recovery is
// implemented by ParcelOfflineReceiptRecovery; this entry never reads or writes.
public static class ParcelLegacyReceiptRecovery
{
    public static Task<object> RecoverAsync(string database,string expectedHash,string backup,string occurrence,string originalRelease,string reason,FlowDefinition definition,Func<string,string,string,CancellationToken,Task<StageReply?>> originalQuery,Action<string,ParcelConfiguration>? registerBackup=null,CancellationToken token=default,string? expectedWalHash=null,int targetStorageVersion=5) => Task.FromException<object>(
        new InvalidDataException("旧格式回执迁移不受支持；保留原请求、原报文和原数据库。"));
}
