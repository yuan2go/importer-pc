using System.Runtime.Versioning;

namespace Ipc.Platform;

// A held capability: source supervision cannot advance while business handoff and
// deployment registration publish. It grants neither a budget reset nor launch.
public sealed class DesktopRecoveryMigration : IDisposable
{
    readonly DeploymentLocation source;
    readonly string targetRoot;
    readonly Action verify;
    readonly List<IDisposable> owners;
    string? registrationFile, registrationHash;
    bool disposed;
    public string? LedgerSha256 { get; }
    internal DesktopRecoveryMigration(DeploymentLocation source, string targetRoot, string? hash, Action verify, params ResourceLease[] owners)
    { this.source=source;this.targetRoot=Path.GetFullPath(targetRoot);LedgerSha256=hash;this.verify=verify;this.owners=[..owners]; }
    internal void HoldRegistration(FileStream owner,string file,string hash)
    { owners.Insert(0,owner);registrationFile=file;registrationHash=hash; }
    internal void RequireRegistration(string file,string hash)
    { ObjectDisposedException.ThrowIf(disposed,this);if(file!=registrationFile||hash!=registrationHash)throw new InvalidOperationException("Migration does not hold the reviewed registration"); }
    internal void Require(DeploymentLocation current,string target)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Wire.Encode(current)!=Wire.Encode(source) || Path.GetFullPath(target)!=targetRoot) throw new InvalidOperationException("Supervision migration no longer matches the reviewed deployment");
        verify();
    }
    public void Dispose()
    {
        if(disposed)return;List<Exception> errors=[];
        foreach(var owner in owners.AsEnumerable().Reverse())try{owner.Dispose();}catch(Exception e){errors.Add(e);}
        if(errors.Count!=0)throw new AggregateException("Recovery migration ownership release failed",errors);
        disposed=true;
    }
}
public static partial class DesktopRecoverySupervisor
{
    internal static DesktopRecoveryMigration HoldMigration(DeploymentLocation source,string targetRoot)
    {
        var sourceDirectory=StateDirectory(source);DeploymentPaths.RequireOrdinaryAncestors(sourceDirectory);
        var exists=true;
        try{_=File.GetAttributes(sourceDirectory);}catch(FileNotFoundException){exists=false;}catch(DirectoryNotFoundException){exists=false;}
        if(!exists && source.Recovery?.Enabled!=true)
        {
            var targetDirectory=StateDirectory(source with {DataRoot=Path.GetFullPath(targetRoot)});
            void RequireAbsent()
            {
                foreach(var path in new[]{sourceDirectory,targetDirectory})
                {
                    DeploymentPaths.RequireOrdinaryAncestors(path);
                    try{_=File.GetAttributes(path);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
                    throw new InvalidOperationException("Unowned supervision evidence cannot be adopted by a disabled migration");
                }
            }
            RequireAbsent();return new(source,targetRoot,null,RequireAbsent);
        }
        RequireDirectory(source);
        var sourceOwner=new ResourceLease(sourceDirectory,["supervisor"],createDirectory:false);ResourceLease? targetOwner=null;
        try
        {
            var state=Read(source);
            if(state.LaunchUncertain || state.Status is "Running" or "Launching") throw new InvalidOperationException("Original supervision must have an observed exit before migration; no kill or inferred reset");
            targetRoot=Path.GetFullPath(targetRoot);DeploymentPaths.RequireOrdinaryAncestors(targetRoot);
            if((File.GetAttributes(targetRoot)&FileAttributes.Directory)==0 || targetRoot==Path.GetFullPath(source.DataRoot)) throw new InvalidDataException("Migration requires an existing distinct restored root");
            var target=source with {DataRoot=targetRoot};var directory=StateDirectory(target);
            DeploymentPaths.RequireOrdinaryAncestors(directory);
            var bytes=File.ReadAllBytes(StateFile(source));var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var targetExists=true;
            try{_=File.GetAttributes(directory);}catch(FileNotFoundException){targetExists=false;}catch(DirectoryNotFoundException){targetExists=false;}
            if(!targetExists)
            {
                var temporary=directory+".migration-"+Wire.Id();Directory.CreateDirectory(Path.Combine(temporary,"diagnostics"));
                using(var output=new FileStream(Path.Combine(temporary,"state.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
                {output.Write(bytes);output.Flush(true);}
                Directory.Move(temporary,directory);
            }
            RequireDirectory(target);targetOwner=new ResourceLease(directory,["supervisor"],createDirectory:false);
            void Verify()
            { _=Read(source);_=Read(target);if(Wire.FileHash(StateFile(source))!=hash || Wire.FileHash(StateFile(target))!=hash)throw new InvalidDataException("Migration supervision evidence differs; originals retained"); }
            Verify();return new(source,targetRoot,hash,Verify,sourceOwner,targetOwner);
        }
        catch(Exception error)
        {
            List<Exception> errors=[error];
            try{targetOwner?.Dispose();}catch(Exception e){errors.Add(e);}
            try{sourceOwner.Dispose();}catch(Exception e){errors.Add(e);}
            if(errors.Count>1)throw new AggregateException("Migration preparation and release failed",errors);
            throw;
        }
    }
}
public static partial class WindowsDeploymentSetup
{
    [SupportedOSPlatform("windows")]
    public static DesktopRecoveryMigration PrepareRecoveryMigration(DeploymentLocation source,string targetRoot,string expectedRegistrationHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var file=Registration(source.ApplicationId);
        var owner=new FileStream(Path.Combine(MachineRoot,"deployments",source.ApplicationId+".lease"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            if(Wire.FileHash(file)!=expectedRegistrationHash || Wire.Encode(Current(source.ApplicationId))!=Wire.Encode(source))
                throw new InvalidOperationException("Reviewed deployment changed before migration; no business fence written");
            if(source.LifecycleState!="Installed") throw new InvalidOperationException("Only an installed deployment can begin data-root migration; no business fence written");
            var migration=DesktopRecoverySupervisor.HoldMigration(source,targetRoot);
            migration.HoldRegistration(owner,file,expectedRegistrationHash);return migration;
        }
        catch{owner.Dispose();throw;}
    }
}
