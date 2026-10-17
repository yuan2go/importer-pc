using Ipc.Platform;
using Ipc.Assembly;

var options = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : throw new ArgumentException("Missing option value"));
if (options.Keys.Any(x => x is not ("--config" or "--data-root" or "--view" or "--layout"))) throw new ArgumentException("Unknown assembly option");
var launch = new BusinessLaunch(options.GetValueOrDefault("--config", Path.Combine(AppContext.BaseDirectory, "config")),
    options.GetValueOrDefault("--data-root", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data")),
    options.GetValueOrDefault("--view"), options.GetValueOrDefault("--layout"));
await using var business = InstalledBusiness.Create().Create(launch);
await business.StartAsync(CancellationToken.None);
Console.WriteLine(Wire.Encode(new { Business = business.Id, business.Presentation, Status = "Started" }));
string? line;
while ((line = Console.ReadLine()) is not null && line != "quit")
{
    try { Console.WriteLine(line == "snapshot" ? Wire.Encode(await business.Application.QueryAsync()) : Wire.Encode(await business.Application.CommandAsync(Wire.Decode<BusinessCommand>(line)))); }
    catch (Exception ex) { Console.WriteLine(Wire.Encode(new { Status = "Failed", Error = ex.GetType().Name, ex.Message })); }
}
await business.StopAsync(CancellationToken.None);
