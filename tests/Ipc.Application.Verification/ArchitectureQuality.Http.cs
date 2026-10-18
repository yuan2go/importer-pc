using System.Net;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Reference.Protocol;

static partial class ArchitectureQuality
{
    static async Task HttpChecks()
    {
        using var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var binding = B("plc") with { Parameters = new() { ["endpoint"] = $"http://127.0.0.1:{port}/" } };
        await using var peer = new ReferencePeer(binding, "", "plc-submit"); await peer.StartAsync(default);
        var payload = Wire.Encode(new ParcelPayload("00001", "http-parcel", new(1.234m, WeightUnit.Kg, 3, WeightSource.Configured, null, "Synthetic configured weight"), null, null));
        var request = new FrozenRequest(Wire.Id(), "direct-plc", Wire.Id(), "http-parcel", "plc-submit", binding, new string('A', 64), new string('B', 64), payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync(); using var input = new StreamReader(context.Request.InputStream); _ = await input.ReadToEndAsync();
            context.Response.SendChunked = true;
            try { for (int i = 0; i < 50; i++) { await context.Response.OutputStream.WriteAsync(new byte[4096]); await context.Response.OutputStream.FlushAsync(); } }
            catch (Exception e) when (e is IOException or HttpListenerException) { }
            finally { context.Response.Close(); }
        });
        try { await peer.SubmitAsync(request, new CancellationTokenSource(3000).Token); Check(false, "oversized chunked reference response must reject"); }
        catch (InvalidDataException) { Check(true, "HTTP submit enforces streamed response quota without Content-Length"); }
        await server.WaitAsync(TimeSpan.FromSeconds(4));
        var slow = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync(); using var input = new StreamReader(context.Request.InputStream); _ = await input.ReadToEndAsync();
            context.Response.SendChunked = true;
            try { await context.Response.OutputStream.WriteAsync("{"u8.ToArray()); await context.Response.OutputStream.FlushAsync(); await Task.Delay(400); }
            catch (Exception e) when (e is IOException or HttpListenerException) { }
            finally { context.Response.Close(); }
        });
        using var budget = new CancellationTokenSource(120);
        try { await peer.QueryAsync(request, budget.Token); Check(false, "stalled response body must observe original cancellation"); }
        catch (OperationCanceledException) { Check(budget.IsCancellationRequested, "HTTP header/body reads share caller cancellation without successful fallback"); }
        await slow.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
