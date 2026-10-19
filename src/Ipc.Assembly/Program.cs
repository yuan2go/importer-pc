using Ipc.Platform;
using Ipc.Assembly;

#if PARCEL
if (ParcelStartupServices.Handle(args)) return;
if (ParcelRuntimeSetup.HandleInstall(args)) return;
if (ParcelCli.Management(args)) return;
#endif
var options = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : throw new ArgumentException("Missing option value"));
if (options.Keys.Any(x => x is not ("--config" or "--data-root" or "--view" or "--layout"))) throw new ArgumentException("Unknown assembly option");
#if PARCEL
var installed = DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, ParcelAssembly.Definition().Id);
#else
DeploymentLocation? installed = null;
#endif
var launch = new BusinessLaunch(options.GetValueOrDefault("--config", installed?.ConfigurationDirectory ?? Path.Combine(AppContext.BaseDirectory, "config")),
    options.GetValueOrDefault("--data-root", installed?.DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data")),
    options.GetValueOrDefault("--view"), options.GetValueOrDefault("--layout"));
await using var business = InstalledBusiness.Create().Create(launch);
await business.StartAsync(CancellationToken.None);
Console.WriteLine(Wire.Encode(new { Business = business.Id, business.Presentation, Status = "Started" }));
string? line;
while ((line = Console.ReadLine()) is not null && line != "quit")
{
    try {
#if PARCEL
        Console.WriteLine(await ParcelCli.Command((Ipc.Application.Contracts.IParcelApplication)business.Application, line));
#else
        Console.WriteLine(line == "snapshot" ? Wire.Encode(await business.Application.QueryAsync()) : Wire.Encode(await business.Application.CommandAsync(Wire.Decode<BusinessCommand>(line))));
#endif
    }
    catch (Exception ex) { Console.WriteLine(Wire.Encode(new { Status = "Failed", Error = ex.GetType().Name, ex.Message })); }
}
await business.StopAsync(CancellationToken.None);
