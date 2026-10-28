using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static partial class OperationClosureVerification
{
    // Test-only proxy: real request-abort tokens distinguish cancelled health checks
    // from failed business replies. Kestrel drains requests before closing connections.
    sealed class QueryEndpoint : IAsyncDisposable
    {
        readonly WebApplication host;
        readonly HttpClient client = new(new SocketsHttpHandler { UseProxy = false });
        readonly ConcurrentQueue<Exception> failures = new();
        readonly ConcurrentQueue<string> cancelledHealth = new();
        readonly string upstream;
        readonly Func<HttpContext, Task>? beforeWrite;
        readonly bool gateQueries;
        int queries, queryReplies, active, disposed, chunkedPosts;
        public int Active => Volatile.Read(ref active);
        public int QueryReplies => Volatile.Read(ref queryReplies);
        public int CancelledHealth => cancelledHealth.Count;
        public int ChunkedPosts => Volatile.Read(ref chunkedPosts);
        public string Url { get; }
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource[] Release { get; } = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];

        public QueryEndpoint(string upstream, Func<HttpContext, Task>? beforeWrite = null, bool gateQueries = true)
        {
            this.upstream = upstream; this.beforeWrite = beforeWrite; this.gateQueries = gateQueries;
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
            host = builder.Build();
            host.Lifetime.ApplicationStopping.Register(() => Stopping.TrySetResult());
            host.Run(Handle);
            try
            {
                host.StartAsync().GetAwaiter().GetResult();
                Url = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/') + "/";
            }
            catch (Exception startup)
            {
                try { host.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception cleanup) { throw new AggregateException("Query fixture startup and cleanup failed", startup, cleanup); }
                finally { client.Dispose(); }
                throw;
            }
        }

        async Task Handle(HttpContext context)
        {
            Interlocked.Increment(ref active);
            var path = context.Request.Path.Value!.TrimStart('/');
            var method = context.Request.Method;
            var health = method == "GET" && path == "health";
            var phase = "upstream";
            var slot = -1;
            var expectedDrop = false;
            try
            {
                using var message = new HttpRequestMessage(new HttpMethod(method), upstream + path);
                if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
                {
                    if (method == "POST" && context.Request.ContentLength is null) Interlocked.Increment(ref chunkedPosts);
                    using var reader = new StreamReader(context.Request.Body);
                    var body = await reader.ReadToEndAsync(context.RequestAborted);
                    message.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    expectedDrop = method == "POST" && path == "requests" &&
                        Wire.Decode<FixturePayload>(Wire.Decode<RequestEnvelope>(body).Payload).Fault == "Drop";
                }
                using var response = await client.SendAsync(message, context.RequestAborted);
                var text = await response.Content.ReadAsStringAsync(context.RequestAborted);
                if (gateQueries && method == "GET" && path.StartsWith("requests/", StringComparison.Ordinal))
                {
                    slot = Interlocked.Increment(ref queries) - 1;
                    if (slot >= Release.Length) throw new InvalidOperationException("Unexpected extra original query");
                    if (slot == 1) BothStarted.TrySetResult();
                    phase = "query-gate";
                    await Release[slot].Task.WaitAsync(context.RequestAborted);
                    if (slot == 0) text = Wire.Encode(Wire.Decode<IntegrationResponse>(text) with { Result = "Rejected", Reason = "AC15 Q1 rejection" });
                }
                phase = "before-response";
                if (beforeWrite is not null) await beforeWrite(context);
                phase = "response-write";
                context.Response.StatusCode = (int)response.StatusCode;
                await context.Response.WriteAsync(text, context.RequestAborted);
                await context.Response.CompleteAsync();
                if (slot >= 0) Interlocked.Increment(ref queryReplies);
            }
            catch (OperationCanceledException) when (health && context.RequestAborted.IsCancellationRequested)
            { cancelledHealth.Enqueue(method + " " + path + " " + phase); }
            catch (IOException) when (health && phase == "response-write" && context.RequestAborted.IsCancellationRequested)
            { cancelledHealth.Enqueue(method + " " + path + " " + phase); }
            catch (HttpRequestException) when (expectedDrop && phase == "upstream")
            { context.Abort(); } // The upstream deliberately commits then drops this POST.
            catch (Exception error)
            {
                var diagnostic = new InvalidOperationException($"Query fixture {method} {path} slot={slot} phase={phase} aborted={context.RequestAborted.IsCancellationRequested}", error);
                failures.Enqueue(diagnostic);
                Console.WriteLine("QUERY_FIXTURE_FAILURE " + diagnostic.Message + ": " + error.GetType().Name);
                context.Abort();
            }
            finally { Interlocked.Decrement(ref active); }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            List<Exception> errors = [];
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                // Never release a query gate to manufacture a successful response during cleanup.
                await host.StopAsync(deadline.Token);
                if (deadline.IsCancellationRequested || Active != 0) errors.Add(new TimeoutException("Query fixture did not drain within its stop budget"));
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                try { await host.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
                client.Dispose();
            }
            errors.AddRange(failures);
            if (errors.Count != 0) throw new AggregateException("Query fixture failed; no transport fault is converted to business success", errors);
        }
    }

    public static async Task QueryEndpointLifetime(string root, string upstream)
    {
        using var caller = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        await using (var rig = await Rig.New(root, upstream))
        await using (var endpoint = new QueryEndpoint(upstream))
        {
            await rig.Command("SetFixtureFault", value: "Drop");
            await rig.Command("OpenContext", value: "000001");
            var request = rig.Store.Requests().Single();
            var dropped = false;
            try
            {
                using var response = await caller.PostAsJsonAsync(endpoint.Url + "requests",
                    new RequestEnvelope(request.Id, request.PayloadHash, request.Binding.Contract, request.Payload), Wire.Json);
            }
            catch (HttpRequestException) { dropped = true; }
            var evidence = await caller.GetFromJsonAsync<IntegrationResponse>(upstream + "requests/" + request.Id, Wire.Json);
            Assert(dropped && endpoint.ChunkedPosts > 0 && evidence?.RequestId == request.Id && evidence.PayloadHash == request.PayloadHash,
                "chunked POST reaches the real fixture before deliberate Drop; abort alone is not proof of acceptance");
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var endpoint = new QueryEndpoint(upstream, async context =>
        { entered.TrySetResult(); await release.Task.WaitAsync(context.RequestAborted); }))
        {
            var response = caller.GetStringAsync(endpoint.Url + "health");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = endpoint.DisposeAsync().AsTask();
            await endpoint.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(!stopping.IsCompleted && endpoint.Active == 1, "query fixture stop waits for an admitted response instead of closing it");
            release.SetResult();
            Assert(await response == "SIMULATION-v1", "admitted health response completes while fixture drains");
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(endpoint.Active == 0, "query fixture drain leaves no active handlers");
        }

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var visits = 0;
        await using (var endpoint = new QueryEndpoint(upstream, async context =>
        {
            if (Interlocked.Increment(ref visits) != 1) return;
            using var registration = context.RequestAborted.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }))
        {
            using var stop = new CancellationTokenSource();
            var request = caller.GetAsync(endpoint.Url + "health", stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
            try { using var response = await request; throw new Exception("Cancelled client unexpectedly succeeded"); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(await caller.GetStringAsync(endpoint.Url + "health") == "SIMULATION-v1", "cancelled health request does not disable the next real health request");
            await endpoint.DisposeAsync();
            Assert(endpoint.CancelledHealth == 1 && endpoint.Active == 0, "actual client abort is recorded only as cancelled health and all handlers terminate");
        }

        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var endpoint = new QueryEndpoint(upstream, async context =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, context.RequestAborted); }, gateQueries: false))
        {
            using var stop = new CancellationTokenSource();
            var request = caller.GetAsync(endpoint.Url + "requests/missing", stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
            try { using var response = await request; throw new Exception("Cancelled query unexpectedly succeeded"); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            var reported = false;
            try { await endpoint.DisposeAsync(); }
            catch (AggregateException error) { reported = error.ToString().Contains("GET requests/missing", StringComparison.Ordinal); }
            Assert(reported && endpoint.CancelledHealth == 0 && endpoint.Active == 0,
                "cancelled original query is a fixture failure, never an expected health disconnect");
        }

        foreach (var path in new[] { "health", "requests/missing" })
        {
            await using var endpoint = new QueryEndpoint(upstream, _ => throw new IOException("synthetic unexpected write failure"), gateQueries: false);
            try { using var response = await caller.GetAsync(endpoint.Url + path); }
            catch (HttpRequestException) { } // The fixture must still report its own fault below.
            var reported = false;
            try { await endpoint.DisposeAsync(); }
            catch (AggregateException error)
            { reported = error.ToString().Contains("synthetic unexpected write failure", StringComparison.Ordinal) && error.ToString().Contains("GET " + path, StringComparison.Ordinal); }
            Assert(reported && endpoint.CancelledHealth == 0 && endpoint.Active == 0,
                "unexpected " + path + " failure remains visible after cleanup; it is not an expected disconnect");
        }
    }
}
