using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Ipc.Platform;
using Ipc.Assembly;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
var checks = new List<string>();
var identity = typeof(MachineRuntimePolicy).GetMethod("ValidateRuntimeAccount", BindingFlags.Static|BindingFlags.NonPublic)!;
void Account(string current, string selected, IEnumerable<Claim> claims, bool denied) {
    try { identity.Invoke(null, [current,selected,true,claims]); if(denied) throw new Exception("Wrongly allowed identity"); }
    catch(TargetInvocationException e) when(e.InnerException is UnauthorizedAccessException && denied) { }
}
var sid="S-1-5-21-100-200-300-1001";
Account(sid,sid,[],false);
Account(sid,sid,[new Claim(ClaimTypes.DenyOnlySid,"S-1-5-32-544")],false);
Account(sid,sid,[new Claim(ClaimTypes.GroupSid,"S-1-5-32-544")],true);
Account(sid,"S-1-5-21-100-200-300-1002",[],true);
foreach(var service in new[]{"S-1-5-18","S-1-5-19","S-1-5-20"}) Account(service,service,[],true);
checks.Add("7 synthetic identity policy cases; no Windows token/ACL claim");
ParcelCapabilityFactory.RegisterContracts();
var temporary=Path.Combine(Path.GetTempPath(),"ipc-release-probe-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var config=Path.Combine(temporary,"config");Directory.CreateDirectory(config);
var seed=Path.Combine(config,"business.json");File.Copy(Path.Combine(args[0],"app/config/business.json"),seed);
var original=File.ReadAllBytes(seed);
var launch=new BusinessLaunch(config,Path.Combine(temporary,"data"),null,null);
var maintenance=new ParcelStartupMaintenance(launch,ParcelAssembly.Definition(),()=>new ParcelCapabilityFactory(launch),()=>{});
var read=await maintenance.ReadAsync();
if(read.Candidate is null || read.ExistingConfiguration) throw new Exception("Seed not independently readable");
var valid=await maintenance.ValidateAsync(read.Candidate);
if(valid.Status!="Validated") throw new Exception("Reference candidate invalid: "+valid);
var pending=await maintenance.StageAsync(read.ConfigurationHash,read.Candidate,"Synthetic release probe only");
if(!pending.ExistingConfiguration || pending.Revision<1 || !File.ReadAllBytes(seed).SequenceEqual(original)) throw new Exception("Offline stage lost seed/ledger");
try { await maintenance.StageAsync(read.ConfigurationHash,read.Candidate,"Stale revision probe"); throw new Exception("Stale CAS accepted"); }
catch(InvalidOperationException) { }
File.WriteAllText(seed,"{broken");
var bad=await maintenance.ReadAsync();
if(bad.Candidate is not null || File.ReadAllText(seed)!="{broken") throw new Exception("Corrupt source overwritten");
checks.Add("Offline read/validate/Pending stage/stale CAS/corruption preservation; synthetic authority, no device runtime");
if(args.Length>1) {
 var media=DeploymentMedia.Inspect(args[1],"sportmaster");
 if(media.SourceCommit!="7acbb2c13d6b35d4aeff8b075b2ff52372aa2042") throw new Exception("Media source differs");
 checks.Add("Actual Windows media inspected by frozen Platform code on Linux; no install/UAC/ACL evidence");
}
Console.WriteLine(JsonSerializer.Serialize(new{Status="PASS",SourceCommit="7acbb2c13d6b35d4aeff8b075b2ff52372aa2042",Checks=checks,Windows="NOT_RUN",SyntheticAuthority=true,Temporary=temporary}));
