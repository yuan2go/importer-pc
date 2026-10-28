using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;

static class StartupStorageBoundaries
{
    sealed record Candidate(int Value);
    static void Check(bool yes,string message){if(!yes)throw new Exception("FAIL WT024 "+message);Console.WriteLine("PASS WT024 "+message);}
    public static async Task Run(string workspace,string root)
    {
        Directory.CreateDirectory(root);
        foreach(var error in new Exception[]{new IOException("Synthetic SDK IO"),new UnauthorizedAccessException("Synthetic SDK denial"),new InvalidDataException("Synthetic rejected business candidate")})
        {
            var file=Path.Combine(root,Wire.Id(),"configuration.json");var notifications=0;
            var c=new ConfigurationRevisions<Candidate>(file,new(1),_=>[],_=>notifications++);c.Stage(0,new(2));
            Exception? seen=null;try{c.BeginActivation(_=>true,_=>throw error);}catch(Exception e){seen=e;}
            Check(ReferenceEquals(seen,error)&&notifications==0&&c.Current.Status=="ActivationFailed"&&c.Current.Effective.Value==1,"configuration callback "+error.GetType().Name+" is not misreported as persistence failure");
        }
        {
            var file=Path.Combine(root,"locked","configuration.json");Exception? notified=null;
            var c=new ConfigurationRevisions<Candidate>(file,new(1),_=>[],e=>notified=e);var before=File.ReadAllBytes(file);Exception? seen=null;
            File.Move(file,file+".retained");Directory.CreateDirectory(file);
            try{c.BeginEffectiveStartup();}catch(Exception e){seen=e;}
            Check(seen is IOException or UnauthorizedAccessException && ReferenceEquals(notified,seen)&&File.ReadAllBytes(file+".retained").SequenceEqual(before)&&Directory.Exists(file)&&c.Current.Confirmation!.State=="Required","actual configuration publication failure notifies owner and retains original authority");
        }
        var config=Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace,"tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with {Login=null,BackupSchedule=null};
        foreach(var mode in new[]{"seed-denied","configuration-locked","sdk-denied","invalid-seed"})
        {
            var directory=Path.Combine(root,mode);Directory.CreateDirectory(directory);var seed=Path.Combine(directory,"business.json");
            if(mode=="seed-denied")Directory.CreateDirectory(seed);else File.WriteAllText(seed,mode=="invalid-seed"?"{invalid seed":Wire.Encode(config));
            var launch=new BusinessLaunch(directory,Path.Combine(directory,"data"),null,null);FileStream? held=null;
            if(mode=="configuration-locked")
            {
                var ledger=Path.Combine(launch.DataRoot,config.Mode.ToString(),config.StationId,config.ApplicationId,"configuration.json");
                _=new ConfigurationRevisions<ParcelConfiguration>(ledger,config,new SportmasterFlow().Validate);held=new(ledger,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            }
            await using var app=new ParcelRuntime(launch,new SportmasterFlow(),new Factory(launch,mode=="sdk-denied")){RuntimeIdentityPreflight=()=>{}};
            Exception? failure=null;try{await app.StartAsync(default);}catch(Exception e){failure=e;}finally{held?.Dispose();}
            Check(failure is not null,"actual startup refuses "+mode);
            if(mode is "seed-denied" or "configuration-locked") Check(app.FatalFailure.IsCompletedSuccessfully && (await app.FatalFailure).PreventRestart==(mode=="seed-denied"),"actual startup file fault completes correctly classified fatal evidence: "+mode);
            else Check(!app.FatalFailure.IsCompleted,"SDK/business rejection does not create false storage fatal: "+mode);
        }
    }
    sealed class Factory(BusinessLaunch launch,bool deny):IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner=new(launch);
        public ParcelConfiguration Normalize(ParcelConfiguration c)=>deny?throw new UnauthorizedAccessException("Synthetic SDK configuration access"):inner.Normalize(c);
        public void Validate(ParcelConfiguration c)=>inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c)=>inner.ValidateStructure(c);
        public ParcelPorts Create(ParcelConfiguration c)=>inner.Create(c);
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
