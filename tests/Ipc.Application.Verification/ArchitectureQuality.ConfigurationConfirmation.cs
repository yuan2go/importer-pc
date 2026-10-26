using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;
using System.Text;

static partial class ArchitectureQuality
{
    public static async Task ConfigurationConfirmationChecks(string source,string evidence)
    {
        workspace=source;root=evidence;failures=0;
        await StartupRoutingChecks();
        var c=Config("sportmaster");
        string Authority(string name)=>Path.Combine(DirectoryFor(name,c),"configuration.json");
        foreach(var priorActivation in new[]{false,true})
        {
            var name="unsupported-envelope-"+priorActivation;
            await using(var initial=await Open(c,name)) { }
            var old=new ConfigurationRevision<ParcelConfiguration>(17,c,null,"Effective",null,
                priorActivation ? new("historical",DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow,"Activated") : null);
            File.WriteAllText(Authority(name),Wire.Encode(old),new UTF8Encoding(true));
            var bytes=File.ReadAllBytes(Authority(name));var originals=CustodyFiles(name,c);var constructed=0;
            try { await using var ignored=await Open(c,name,f=>{f.Boundary=_=>constructed++;return f;});Check(false,"unsupported envelope must refuse"); }
            catch(InvalidDataException)
            { Check(constructed==0 && File.ReadAllBytes(Authority(name)).SequenceEqual(bytes) && originals.All(p=>Wire.FileHash(p.Key)==p.Value),
                "missing confirmation refuses before resources and preserves BOM, prior activation and original files; no inferred effectiveness"); }
        }
        const string failedName="confirmation-seed-failed";
        try { await using var ignored=await Open(c,failedName,f=>new FailingConfirmationFactory(f));Check(false,"seed startup must fail"); }
        catch(InvalidOperationException error) when(error.Message=="Synthetic Create failure") { }
        var failed=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority(failedName)));
        Check(failed.Confirmation is {Required:true,Source:"Seed",State:"Required"} && failed.Confirmation.Attempts is [{Outcome:"Failed",FinishedAt:not null}],
            "current seed retains failed resource attempt without claiming Effective");
        var ledger=new ConfigurationRevisions<ParcelConfiguration>(Authority(failedName),c,new Ipc.Application.Sportmaster.SportmasterFlow().Validate);
        try { ledger.ConfirmEffective(ledger.Current.Revision,ledger.Current.Confirmation!.EffectiveHash,"synthetic-admin","Cannot bypass failed resources");Check(false,"confirmation cannot bypass resource startup"); }
        catch(InvalidOperationException) { Check(File.ReadAllText(Authority(failedName))==Wire.Encode(failed),"administrative confirmation cannot turn failed resources into success"); }
        await using(var recovered=await Open(c,failedName))
        {
            var state=await recovered.SnapshotAsync();
            Check(state.Operations.Length == 0 && state.Configuration.Confirmation is {Required:false,State:"Confirmed",Source:"Seed"} &&
                state.Configuration.Confirmation.Attempts.Select(a=>a.Outcome).SequenceEqual(new[]{"Failed","ResourcesStarted"}),
                "current successful resource startup confirms its seed, retains failure history and starts no business request");
        }
        var confirmed=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority(failedName)));
        try { await using var ignored=await Open(c,failedName,f=>new FailingConfirmationFactory(f));Check(false,"confirmed restart resource failure expected"); }
        catch(InvalidOperationException error) when(error.Message=="Synthetic Create failure") { }
        var afterFailure=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority(failedName)));
        Check(Wire.Encode(afterFailure.Confirmation)==Wire.Encode(confirmed.Confirmation),"ordinary restart failure preserves original confirmed evidence");
        // A real process terminates after durable BeginEffectiveStartup and before resource construction.
        const string cutName="confirmation-interrupted";
        var path=Path.Combine(root,cutName);Directory.CreateDirectory(path);ExtensionStore.AtomicWrite(Path.Combine(path,"business.json"),c);
        var psi=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true};
        psi.ArgumentList.Add(typeof(ArchitectureQuality).Assembly.Location);psi.ArgumentList.Add(workspace);
        psi.ArgumentList.Add("--configuration-confirmation-crash-child");psi.ArgumentList.Add(path);
        using(var child=Process.Start(psi)!)
        {
            var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));Console.Write(await stdout);Console.Write(await stderr);
            var interrupted=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority(cutName)));
            Check(child.ExitCode==73 && interrupted.Confirmation is {Required:true,State:"InProgress",Attempts:[{FinishedAt:null}]},
                "real process termination retains current unfinished resource attempt");
        }
        await using(var reopened=await Open(c,cutName))
        {
            var state=await reopened.SnapshotAsync();
            Check(state.Operations.Length == 0 && state.Configuration.Confirmation is {Required:false,State:"Confirmed"} &&
                state.Configuration.Confirmation.Attempts.Select(a=>a.Outcome).SequenceEqual(new[]{"Interrupted","ResourcesStarted"}),
                "restart records interruption and confirms only after actual current resources start; no business replay");
        }
        // Current BOM input remains readable; accepting its encoding does not invent missing metadata.
        File.WriteAllText(Authority(cutName),File.ReadAllText(Authority(cutName)),new UTF8Encoding(true));
        await using(var reopened=await Open(c,cutName)) Check((await reopened.SnapshotAsync()).Configuration.Confirmation?.Required==false,"current metadata with UTF8 BOM remains supported");
        if(failures!=0)throw new Exception("Configuration confirmation failures: "+failures);
        Console.WriteLine("PASS CURRENT CONFIGURATION CONFIRMATION COMPLETE");
    }
    sealed class FailingConfirmationFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);
        public void Validate(ParcelConfiguration c)=>inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)=>throw new InvalidOperationException("Synthetic Create failure");
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
