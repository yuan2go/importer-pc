using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Reference.Protocol;
namespace Ipc.Fixture;

// A separate loopback-only process with its own durable receiver root. No device access.
static class ParcelPeerEndpoints
{
    public static void Map(WebApplication app, string directory)
    {
        var gate = new SemaphoreSlim(1, 1);
        app.MapGet("/parcel-reference/health", () => Results.Text("PARCEL-REFERENCE-v1"));
        foreach (var action in new[] { "submit", "query" })
            app.MapPost("/parcel-reference/" + action, async (HttpContext context) =>
            {
                var exchange = await context.Request.ReadFromJsonAsync<ReferenceExchange>(Wire.Json, context.RequestAborted).ConfigureAwait(false);
                if (exchange?.Request is not { } request || request.Stage is not ("image-upload" or "wcs-slot" or "plc-submit") ||
                    exchange.Content?.Length > 5592408 || !Uri.TryCreate(request.Binding.Target, UriKind.Absolute, out var target) || target.Scheme != "reference")
                    return Results.BadRequest();
                // Serialize ownership of the synthetic ledger, including changed parameters.
                // Original request equality is still checked by the persistent receiver.
                await gate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                try
                {
                    await using var peer = new ReferencePeer(request.Binding, directory, request.Stage, serveLocally: true);
                    await peer.StartAsync(context.RequestAborted).ConfigureAwait(false);
                    StageReply? reply = action == "query" ? await peer.QueryAsync(request, context.RequestAborted).ConfigureAwait(false)
                        : await peer.SubmitAsync(request, context.RequestAborted, exchange.Content is null ? default : Convert.FromBase64String(exchange.Content)).ConfigureAwait(false);
                    return Results.Json(reply, Wire.Json);
                }
                catch (IOException) { return Results.StatusCode(503); } // May already be committed; query original identity.
                catch (Exception ex) when (ex is InvalidDataException or FormatException) { return Results.Conflict(); }
                finally { gate.Release(); }
            });
    }
}
