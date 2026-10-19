using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Narrow offline transition for the old Attached-in-flight defect. The matching
// original implementation confirms the outcome; the new reader retains raw custody.
public static class ParcelLegacyReceiptRecovery
{
    internal static Action<string>? Boundary;
    public static async Task<object> RecoverAsync(string database,string expectedHash,string backup,string occurrence,string originalRelease,string reason,FlowDefinition definition,Func<string,string,string,CancellationToken,Task<StageReply?>> originalQuery,Action<string,ParcelConfiguration>? registerBackup=null,CancellationToken token=default,string? expectedWalHash=null,int targetStorageVersion=5)
    {
        if (targetStorageVersion is not (5 or 6 or 7 or 8)) throw new ArgumentOutOfRangeException(nameof(targetStorageVersion));
        database=Path.GetFullPath(database);backup=Path.GetFullPath(backup);
        RecoveryFiles.RequireOrdinaryAncestors(database);RecoveryFiles.RequireOrdinaryAncestors(backup);
        var directory=Path.GetDirectoryName(database)!;
        using var owner=new ResourceLease(directory,["application-owner"]);
        if(!ExtensionSchema.IsHash(expectedHash)||Wire.FileHash(database)!=expectedHash||string.IsNullOrWhiteSpace(reason)||reason.Length>1024)
            throw new InvalidDataException("Exact source database hash and inspection reason required");
        if(Path.GetFileName(database)!=definition.Id+".db"||backup.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new InvalidDataException("Use original Application database and separate new backup destination");
        var wal=database+"-wal";
        if(File.Exists(wal)&&new FileInfo(wal).Length>0 && (!ExtensionSchema.IsHash(expectedWalHash)||Wire.FileHash(wal)!=expectedWalHash))
            throw new InvalidDataException("Nonempty original WAL requires its exact --wal-sha256; main file hash alone is incomplete");
        var version=ParcelRepository.ReadStorageVersion(database,definition.Id);
        var envelope=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Path.Combine(directory,"configuration.json")));
        var c=envelope.Effective;
        if(Path.GetFileName(directory)!=definition.Id||Directory.GetParent(directory)?.Name!=c.StationId||Directory.GetParent(directory)?.Parent?.Name!=c.Mode.ToString()||c.ApplicationId!=definition.Id)
            throw new InvalidDataException("Original deployment identity differs");
        var dataRoot=Directory.GetParent(directory)!.Parent!.Parent!.FullName;
        if(registerBackup is null && (c.Extensions?.Packages.Length>0||envelope.Pending?.Extensions?.Packages.Length>0)) throw new InvalidDataException("Extension backup consumer registration required");
        using(var repository=new ParcelRepository(directory,definition.Id,version))
        {
            var original = repository.ReceiptById(occurrence) ?? throw new InvalidDataException("Original receipt occurrence unavailable");
            if (version == 4 && original.State == "OutcomeReconciled")
            {
                // An old release falsely cleared raw custody, sometimes archiving its
                // owner. Verify its existing Query BEFORE SQL5; never ask a new Query
                // to stand in for the old raw or manufacture an already-released image.
                var prior = repository.ReadOperation(original.Operation) ?? throw new InvalidDataException("Original archived owner unavailable");
                if (original.Stage != "image-upload" || original.RawHash is not null ||
                    File.Exists(Path.Combine(directory,"receipts",occurrence+".json")) ||
                    prior.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached ||
                    !repository.HasMainReceiptCustody(prior) || !repository.HasReceiptOutcomeBasis(occurrence))
                    throw new InvalidDataException("Prior outcome custody proof incomplete; original reader marker retained");
                repository.ValidateRawInventory();
                await BackupAsync(repository,directory,backup,envelope,registerBackup).ConfigureAwait(false);
                repository.Commit(null,"LegacyPriorCustodyUpgradePrepared",
                    OutcomeDetail(occurrence,original.QueryOccurrence!,prior,reason,backup,originalRelease),paused:true);
                // Full-set eligibility, including other owners, is checked below while
                // the old marker remains intact. This path makes no external call.
            }
            else
            {
                var pending=repository.PendingReceipts().SingleOrDefault(x=>x.Id==occurrence);
                var op=repository.Load().SingleOrDefault(x=>x.Id==pending.Operation)??throw new InvalidDataException("Original receipt owner unavailable");
                if(pending.Stage!="image-upload"||!repository.MissingAwaitingRaw(occurrence)||op.FrozenConfiguration.Images?.Strategy!=UploadStrategy.Attached||op.Upload?.Request.Id!=pending.Request||op.Upload.State==StageState.Conflict||!repository.HasMainReceiptCustody(op))
                    throw new InvalidDataException("Only an original missing Awaiting Attached upload with committed main raw is supported");
                var restored=definition.RestoreMainProjection(op) with {RecoveryRequired=false};
                if(!definition.MainSettled(restored))throw new InvalidDataException("Original main result is not proven complete");
                repository.ValidateRawInventory(); // Before any external query or future-reader migration.
                await BackupAsync(repository,directory,backup,envelope,registerBackup).ConfigureAwait(false);
                string? basis=repository.ReceiptById(occurrence)!.QueryOccurrence;
                // A previous maintenance process may have flushed Query raw and died before
                // T3. Finish that exact occurrence locally; do not strand it behind a new Query.
                foreach(var local in repository.PendingReceipts().Where(x=>x.Operation==op.Id&&x.Stage=="image-upload"&&x.Reply is not null).ToArray())
                {
                    var raw=local.Reply!;
                    if(!ValidTerminalReply(repository,restored,raw,directory))
                    {
                        repository.RecoverRaw(local.Id,raw);
                        repository.Commit(Conflicted(restored,raw),"LegacyQueryConflict","Original persisted Query is contradictory; no upgrade",paused:true,occurrence:local.Id);
                        return new{Status="Conflict",DatabaseVersion=version,OriginalOccurrence=occurrence};
                    }
                    repository.RecoverRaw(local.Id,raw);restored=ApplyTerminalReply(restored,raw);
                    repository.Commit(restored,"ReceiptOutcomeKnown",OutcomeDetail(occurrence,local.Id,op,reason,backup,originalRelease),paused:true,occurrence:local.Id,reconciledOccurrence:occurrence);
                    basis=local.Id;
                }
                if(!repository.HasReceiptOutcomeBasis(occurrence))
                {
                    var query=repository.BeginReceipt(op,op.Upload,"Query"); StageReply? reply;
                    try { reply=await originalQuery(Wire.Encode(op),directory,dataRoot,token).ConfigureAwait(false); }
                    catch(Exception ex) when(ex is IOException or TimeoutException or OperationCanceledException)
                    { repository.NoReply(query);repository.Commit(null,"LegacyQueryUnavailable",ex.GetType().Name+": "+ex.Message,paused:true);throw; }
                    if(reply is null)
                    { repository.NoReply(query);repository.Commit(null,"LegacyQueryInconclusive","Original Query has no authoritative result; custody unchanged",paused:true);return new{Status="Unresolved",DatabaseVersion=version,OriginalOccurrence=occurrence}; }
                    repository.StoreRaw(query,reply);Boundary?.Invoke("AfterLegacyRawDurable");
                    if(!ValidTerminalReply(repository,restored,reply,directory))
                    {
                        repository.Commit(Conflicted(restored,reply),"LegacyQueryConflict","Original response retained; no upgrade or raw disposition",paused:true,occurrence:query);
                        return new{Status="Conflict",DatabaseVersion=version,OriginalOccurrence=occurrence};
                    }
                    restored=ApplyTerminalReply(restored,reply);
                    repository.Commit(restored,"ReceiptOutcomeKnown",OutcomeDetail(occurrence,query,op,reason,backup,originalRelease),paused:true,occurrence:query,reconciledOccurrence:occurrence);
                    basis=query;Boundary?.Invoke("AfterLegacyOutcomeCommit");
                }
                // An earlier process can also die before receiving a Query reply. The known
                // outcome may be linked to each such occurrence, but NONE loses raw custody.
                foreach(var missing in repository.PendingReceipts().Where(x=>x.Operation==op.Id&&x.Stage=="image-upload"&&x.Reply is null&&!repository.HasReceiptOutcomeBasis(x.Id)).ToArray())
                    repository.Commit(null,"ReceiptOutcomeKnown",OutcomeDetail(missing.Id,basis!,op,reason,backup,originalRelease),paused:true,reconciledOccurrence:missing.Id,outcomeQuery:basis);
                repository.ValidateRawInventory();
            }
            Boundary?.Invoke("BeforeLegacyUpgrade");
        }
        ParcelRepository.UpgradeCustodyOnly(directory,definition.Id,targetStorageVersion);
        using var readback=new ParcelRepository(directory,definition.Id,Math.Max(version,targetStorageVersion));
        return new{Status="UpgradedCustodyRetained",DatabaseVersion=readback.StorageVersion,OriginalOccurrence=occurrence,Raw=readback.ReceiptById(occurrence),Pending=readback.PendingReceipts().Length,Backup=backup,OriginalQueryOnly=true,OriginalSubmitAttemptsUnchanged=true};
    }
    static string OutcomeDetail(string original,string query,ParcelOperation before,string reason,string backup,string release) =>
        Wire.Encode(new{OriginalOccurrence=original,QueryOccurrence=query,Original=before,Actor=Environment.UserName,Reason=reason,Backup=backup,OriginalRelease=Path.GetFullPath(release)});
    static ParcelOperation ApplyTerminalReply(ParcelOperation op,StageReply reply)=>op with
    {Upload=op.Upload! with{State=reply.BusinessAccepted==true?StageState.Accepted:StageState.Rejected,Replies=op.Upload.Replies.Append(reply).Distinct().ToArray(),Failure=null},Image=reply.BusinessAccepted==true?reply.Image:op.Image};
    static ParcelOperation Conflicted(ParcelOperation op,StageReply reply)=>op with
    {Upload=op.Upload! with{State=StageState.Conflict,Replies=op.Upload.Replies.Append(reply).ToArray(),Failure="Invalid/contradictory original Query; original custody retained"}};
    static bool ValidTerminalReply(ParcelRepository repository,ParcelOperation op,StageReply reply,string directory)
    {
        var stage=op.Upload!;var original=op.Image;var terminal=stage.Replies.LastOrDefault(r=>r.BusinessAccepted is not null);
        if(repository.OriginalReplyHash(stage.Request.Id,reply.Id) is {} originalHash && originalHash!=Wire.Hash(Wire.Encode(reply))||
            stage.State==StageState.Conflict||reply.RequestId!=stage.Request.Id||reply.PayloadHash!=stage.Request.PayloadHash||
            !Guid.TryParseExact(reply.Id,"N",out _)||!reply.TransportSucceeded||reply.ProtocolAcknowledged!=true||reply.BusinessAccepted is null||
            reply.PhysicalCompleted==true||string.IsNullOrWhiteSpace(reply.Evidence)||reply.At<stage.Request.PreparedAt||reply.At>DateTimeOffset.UtcNow.AddSeconds(1)||
            stage.Replies.Any(r=>r.Id==reply.Id&&Wire.Encode(r)!=Wire.Encode(reply))||
            terminal is not null&&(terminal.BusinessAccepted!=reply.BusinessAccepted||terminal.Slot!=reply.Slot||Wire.Encode(terminal.Image)!=Wire.Encode(reply.Image)))return false;
        if(original is not null)
        {
            var file=RecoveryFiles.Resolve(directory,original.RelativeFile);
            if(!File.Exists(file)||new FileInfo(file).Length!=original.Length||Wire.FileHash(file)!=original.Sha256)return false;
        }
        return reply.BusinessAccepted!=true||original is not null&&reply.Image is not null&&reply.Image with{UploadedUri=null}==original with{UploadedUri=null}&&
            Uri.TryCreate(reply.Image.UploadedUri,UriKind.Absolute,out var uri)&&uri.Scheme is "reference" or "http" or "https"&&string.IsNullOrEmpty(uri.UserInfo);
    }
    static Task BackupAsync(ParcelRepository repository,string directory,string backup,ConfigurationRevision<ParcelConfiguration> envelope,Action<string,ParcelConfiguration>? registerBackup)
    {
        var recovery=backup+".recovery";
        if(File.Exists(backup)||Directory.Exists(recovery))throw new IOException("Choose a new immutable full backup destination");
        var names=new List<string>{"configuration.json"};names.AddRange(RecoveryFiles.Enumerate(directory,"receipts"));names.AddRange(RecoveryFiles.Enumerate(directory,"images",skipImageLeases:true)); names.AddRange(RecoveryFiles.MaintenanceEvidence(directory));
        if(File.Exists(Path.Combine(directory,ParcelMachineOrigin.FileName)))names.Add(ParcelMachineOrigin.FileName);
        if(File.Exists(Path.Combine(directory,"overload.json")))names.Add("overload.json");
        RecoveryFiles.ValidateNames(directory,names);var hashes=names.ToDictionary(n=>n,n=>Wire.FileHash(RecoveryFiles.Resolve(directory,n)));
        var bytes=checked(repository.StorageBytes+RecoveryFiles.MaximumManifestBytes+names.Sum(n=>new FileInfo(RecoveryFiles.Resolve(directory,n)).Length));
        if(!ParcelStorageVolume.Plan(backup,bytes,envelope.Effective.Retention?.MinimumFreeBytes??67108864).Fits)throw new IOException("Backup volume lacks capacity and reserve");
        // Register the old configuration without loading it through the new ABI. The
        // original host owns validation; registration itself must still retain exact refs.
        registerBackup?.Invoke(backup,envelope.Effective);if(envelope.Pending is{}pending)registerBackup?.Invoke(backup,pending);
        Directory.CreateDirectory(recovery);repository.Backup(backup);
        foreach(var name in names){var target=RecoveryFiles.Resolve(recovery,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory,name),target,hashes[name]);}
        ExtensionStore.AtomicWrite(Path.Combine(recovery,"COMPLETED.json"),new ParcelRecoveryManifest(envelope.Effective.ApplicationId,Path.GetFileName(backup),Wire.FileHash(backup),hashes,DateTimeOffset.UtcNow,"Before original-release Query and custody-only upgrade; all original bytes retained"));
        return Task.CompletedTask;
    }
}
