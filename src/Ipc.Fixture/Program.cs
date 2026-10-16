using Ipc.Fixture;
using Ipc.Platform;
using Ipc.Supply;

var argsMap = args.Chunk(2).ToDictionary(x => x[0], x => x.Length > 1 ? x[1] : "");
var url = argsMap.GetValueOrDefault("--url", "http://127.0.0.1:18743/");
if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1" || uri.Scheme != "http" || uri.AbsolutePath != "/") throw new InvalidDataException("SIMULATION fixture binds literal loopback only");
var data = Path.GetFullPath(argsMap.GetValueOrDefault("--data", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "SimulationFixture")));
Directory.CreateDirectory(data);
using var lease = new ResourceLease(data, ["fixture"]);
using var receiver = new Receiver(Path.Combine(data, "receiver.db"));
var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls(url); builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = null; o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); });
var app = builder.Build(); app.Use(async (context, next) => { if (context.Request.ContentLength > 64000) { context.Response.StatusCode = 413; return; } await next(context); });
app.MapGet("/health", () => Results.Text("SIMULATION-v1"));
app.MapGet("/statistics", () => receiver.Statistics());
app.MapGet("/requests/{id}", (string id) =>
{
    var (response, fault) = receiver.Query(id);
    return fault == "QueryUnsupported" ? Results.StatusCode(501) : response is null || fault == "QueryMissing" ? Results.NotFound() : Results.Json(response, Wire.Json);
});
app.MapPost("/requests", async (RequestEnvelope envelope, HttpContext context) =>
{
    FixtureResponse response;
    try { response = receiver.Process(envelope); }
    catch (InvalidDataException ex) { return Results.Conflict(new { Error = ex.Message }); }
    var payload = Wire.Decode<FixturePayload>(envelope.Payload);
    if (payload.Fault is "Drop" or "QueryMissing" or "QueryUnsupported") { context.Abort(); return Results.Empty; }
    if (payload.Fault == "Delay") await Task.Delay(3000); // Commit first; may outlive the client's timeout/cancellation.
    return Results.Json(response, Wire.Json);
});
await app.RunAsync();
