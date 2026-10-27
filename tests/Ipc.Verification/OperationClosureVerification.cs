using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Product;
using Microsoft.Extensions.Logging.Abstractions;
using Ipc.Desktop;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class OperationClosureVerification
{
    static void Assert(bool condition, string text)
    { if (!condition) throw new Exception("AC15 " + text); Console.WriteLine("PASS AC15 " + text); }
    static void Sql(string path, string sql)
    { using var db = new SqliteConnection("Data Source=" + path); db.Open(); using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    public static async Task Run(string root, string url, string workspace)
    {
        await IndependentReceipts(root, url, false);
        await IndependentReceipts(root, url, true);
        await ConcurrentProductQueries(root, url);
        await BoxResults(root, url);
        await EditingAndTiming(root, url, workspace);
        Console.WriteLine("PASS AC15 OPERATION CLOSURE COMPLETE; WINDOWS_NOT_RUN");
    }
    static async Task Wait(Func<Task<bool>> condition)
    { var watch = Stopwatch.StartNew(); while (!await condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("AC15 condition"); await Task.Delay(20); } }
    static async Task ConcurrentProductQueries(string root, string url)
    {
        var directory = Path.Combine(root, "actual-concurrent-queries"); Directory.CreateDirectory(directory);
        await using var endpoint = new QueryEndpoint(url);
        var config = Rig.Config(endpoint.Url) with { TimeoutMs = 10000 };
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!; var definition = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        var cfg = Path.Combine(directory, "config"); Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg,"station.json"), Wire.Encode(config)); File.WriteAllText(Path.Combine(cfg,"product.json"), Wire.Encode(manifest)); File.WriteAllText(Path.Combine(cfg,"presentation.json"), Wire.Encode(new PresentationSettings()));
        var boot = new BootOptions(Path.Combine(cfg,"product.json"),Path.Combine(cfg,"station.json"),Path.Combine(cfg,"presentation.json"),Path.Combine(directory,"data"),install,Path.Combine(directory,"leases"));
        string requestId;
        await using (var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance))
        {
            await runtime.StartAsync(CancellationToken.None); Assert(runtime.Application is not null && runtime.Diagnostics.Count == 0, "real ProductRuntime starts controlled query endpoint: " + string.Join("; ", runtime.Diagnostics));
            var app = runtime.Application!;
            async Task Command(string kind, string? value = null)
            {
                CommandReceipt? result = null;
                await Wait(async () => { var snapshot = await app.SnapshotAsync(); result = await runtime.CommandAsync(new(Wire.Id(), snapshot.State.Revision, kind, Value:value)); return result.Status != "Conflict"; });
                Assert(result!.Status == "Persisted", "actual product command " + kind);
            }
            await Command("Login", "maintenance");
            await Wait(async () => !(await app.SnapshotAsync()).Admit.Reasons.Any(x => x.StartsWith("Device:") || x.StartsWith("Wcs")));
            await Command("SetFixtureFault", "Drop"); await Command("OpenContext", "000001");
            await Wait(async () => (await app.SnapshotAsync()).Requests.Any(x=>x.Status==RequestStatus.Unknown));
            requestId = (await app.SnapshotAsync()).Requests.Single().Id;
            var q1 = runtime.ReconcileAsync(requestId); var q2 = runtime.ReconcileAsync(requestId);
            await endpoint.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var receipts = Path.Combine(runtime.DataDirectory!,"receipt-ingress");
            if (Directory.Exists(receipts)) Directory.Delete(receipts);
            File.WriteAllText(receipts,"AC15 deliberate custody-create failure");
            endpoint.Release[0].SetResult(); await (await Task.WhenAny(q1, q2));
            File.Delete(receipts);
            endpoint.Release[1].SetResult(); await Task.WhenAll(q1, q2);
            var recovery = (await app.SnapshotAsync()).Recovery.Single(x=>x.Request.Id==requestId);
            Assert(recovery.Request.Status==RequestStatus.Accepted && recovery.Receipts.Length==1 && recovery.Receipts[0].Origin=="original-query",
                "actual concurrent Product/Integration queries retain failed Q1 after Q2 succeeds");
            await runtime.StopAsync(CancellationToken.None);
        }
        await using var restarted = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance);
        await restarted.StartAsync(CancellationToken.None);
        var restored = await restarted.Application!.SnapshotAsync();
        Assert(restored.Recovery.Single(x=>x.Request.Id==requestId).Receipts.Single().Status=="RawUnavailable" && !restored.Admit.Allowed,
            "actual Product controlled stop/restart retains missing original without blind resend or resource loss");
    }
    sealed class QueryEndpoint : IAsyncDisposable
    {
        readonly HttpListener listener = new(); readonly HttpClient client = new(new SocketsHttpHandler { UseProxy=false });
        readonly Task worker; readonly List<Task> calls = []; readonly string upstream; int queries;
        public string Url { get; }
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource[] Release { get; } = [new(TaskCreationOptions.RunContinuationsAsynchronously),new(TaskCreationOptions.RunContinuationsAsynchronously)];
        public QueryEndpoint(string upstream)
        {
            this.upstream = upstream;
            var socket = new TcpListener(IPAddress.Loopback,0); socket.Start(); var port=((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Url=$"http://127.0.0.1:{port}/"; listener.Prefixes.Add(Url); listener.Start();
            worker=Task.Run(async()=> { try { while(listener.IsListening) { var context=await listener.GetContextAsync(); lock(calls)calls.Add(Handle(context)); } } catch(HttpListenerException) when(!listener.IsListening){} catch(ObjectDisposedException) when(!listener.IsListening){} });
        }
        async Task Handle(HttpListenerContext context)
        {
            try
            {
                var path=context.Request.Url!.AbsolutePath.TrimStart('/');
                using var message=new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod),upstream+path);
                if(context.Request.HasEntityBody) { using var reader=new StreamReader(context.Request.InputStream); message.Content=new StringContent(await reader.ReadToEndAsync(),Encoding.UTF8,"application/json"); }
                using var response=await client.SendAsync(message);
                var text=await response.Content.ReadAsStringAsync();
                if(context.Request.HttpMethod=="GET" && path.StartsWith("requests/",StringComparison.Ordinal))
                {
                    var slot=Interlocked.Increment(ref queries)-1; if(slot==1)BothStarted.TrySetResult();
                    await Release[slot].Task;
                    if(slot==0) text=Wire.Encode(Wire.Decode<IntegrationResponse>(text) with { Result="Rejected",Reason="AC15 Q1 rejection" });
                }
                context.Response.StatusCode=(int)response.StatusCode;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(text));
            }
            catch(HttpRequestException) { context.Response.Abort(); return; }
            finally { context.Response.Close(); }
        }
        public async ValueTask DisposeAsync()
        {
            foreach(var signal in Release)signal.TrySetResult(); listener.Stop(); await worker;
            Task[] pending; lock(calls)pending=calls.ToArray(); await Task.WhenAll(pending); client.Dispose(); listener.Close();
        }
    }
    static async Task BoxResults(string root, string url)
    {
        var directory = Path.Combine(root, "box-results"); var config = Rig.Config(url); string sessionId, closeId;
        await using (var h = await Rig.Open(directory, config))
        {
            await h.Command("SetFixtureFault", value: "Reject"); await h.Command("OpenContext", value: "000001"); await h.Drain();
            var s = await h.App.SnapshotAsync();
            Assert(s.State.Session?.Status == SessionStatus.Rejected && s.BoxResult is { Stage: RequestKind.Context, Status: RequestStatus.Rejected } && s.BoxResult.Reason.Length > 0,
                "Context rejection is a persistent box result, not a toast");
            await h.OpenBox(); sessionId = (await h.App.SnapshotAsync()).State.Session!.Id;
            await h.Command("SetFixtureFault", value: "Reject"); await h.Command("CheckClose"); await h.Drain();
            s = await h.App.SnapshotAsync();
            Assert(s.State.Session!.Status == SessionStatus.Active && s.BoxResult is { Stage: RequestKind.CheckClose, Status: RequestStatus.Rejected },
                "rejected CheckClose coexists with true Active state");
            await h.Command("CheckClose"); await h.Drain(); s = await h.App.SnapshotAsync();
            await h.Command("SetFixtureFault", value: "Reject"); await h.Command("ConfirmClose", s.State.Session!.CloseCheckRequest, "CONFIRM"); await h.Drain();
            s = await h.App.SnapshotAsync(); closeId = s.BoxResult!.RequestId;
            Assert(s.State.Session!.Status == SessionStatus.Active && s.BoxResult is { Stage: RequestKind.Close, Status: RequestStatus.Rejected },
                "rejected Close preserves original request/reason alongside Active");
            var op = await h.Enter("left"); await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain(); await h.Command("Submit", op.Id); await h.Drain();
            Assert((await h.App.SnapshotAsync()).BoxResult!.RequestId == closeId && h.Store.AcceptedQuantity(sessionId) == 1,
                "another lane acceptance cannot hide box Close rejection or recount it");
            var query = await h.App.QueryHistoryAsync(new(HistoryEntity.Sessions, SessionId: sessionId));
            Assert(query.Items.Single().AttemptCount == 6 && query.Items.Single().AcceptedQuantity == 1,
                "history summary reads actual request attempts and unique accepted quantity");
            await h.Command("Logout");
            try { await h.App.SessionDetailsAsync(sessionId); throw new Exception("unauthorized history accepted"); }
            catch (GuardException) { Assert(true, "box projection does not grant unauthorized history detail access"); }
        }
        await using var reopened = await Rig.Open(directory, config);
        Assert((await reopened.App.SnapshotAsync()).BoxResult is { RequestId: var id, Status: RequestStatus.Rejected } && id == closeId,
            "box result refresh/restart derives from original persisted request and response");
    }
    internal static async Task EditingAndTiming(string root, string url, string workspace)
    {
        var draft = new ConfigurationDraft(); draft.Replace(Rig.Config(url));
        draft["Timeout"] = ""; draft["EventCapacity"] = "invalid";
        Assert(draft.IntegrationTarget.Endpoint == Rig.Config(url).Integration.Endpoint, "module/new-instance defaults read incomplete draft without parsing unrelated numbers");
        try { draft.Build(); throw new Exception("invalid save accepted"); } catch (InvalidDataException error) { Assert((string?)error.Data["ConfigurationField"] == "Timeout", "save/preview reject incomplete numeric draft with exact field identity for focus"); }
        draft["Timeout"] = "500"; draft["EventCapacity"] = "64"; Assert(draft.Build().TimeoutMs == 500, "corrected draft builds without resetting edits from Effective");
        var effective = Rig.Config(url);
        draft["Timeout"] = "";
        var comparison = draft.Compare(effective).Single(x => x.Field == "Timeout");
        Assert(comparison.Draft == "" && comparison.Effective == effective.TimeoutMs.ToString() && effective.TimeoutMs > 0,
            "draft/effective comparison tolerates temporary invalid text without mutating effective configuration");
        draft["Timeout"] = "500";
        var invalidFields = effective with { TimeoutMs = 0, EventCapacity = 0, Integration = effective.Integration with { Endpoint = "not-an-endpoint" }, Devices = effective.Devices.Select((x, i) => i == 0 ? x with { DeviceId = "" } : x).ToArray() };
        var fieldIssues = ConfigurationValidationPresentation.Locate(effective, invalidFields, SupplyConfiguration.Validate(invalidFields), SupplyConfiguration.Validate);
        Assert(new[] { "Timeout", "EventCapacity", "Endpoint", "Devices.left" }.All(field => fieldIssues.Any(x => x.Field == field)) &&
            !fieldIssues.Any(x => x.Field == "MinimumFree"),
            "real configuration validator locates simultaneous timeout/capacity/endpoint/device errors without duplicating rules or blaming unchanged fields");
        var revision = new ConfigRevision("original-revision", effective.Version, effective with { Version = "next" }, "maintenance", "PendingActivation", "original reason", DateTimeOffset.UtcNow);
        Assert(DeploymentPresentation.Revision(revision).Contains("重启", StringComparison.Ordinal) &&
            DeploymentPresentation.Revision(revision with { Status = "ActivationFailed" }).Contains("保留上一有效", StringComparison.Ordinal) &&
            DeploymentPresentation.Summary([revision]).Contains("已保存 1", StringComparison.Ordinal) && revision.Status == "PendingActivation",
            "lifecycle presents saved/Pending/restart/failure-preserves-old independently without changing deployment state");
        var selection = new SelectionLoad(); var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = ""; var errors = 0; var settled = 0;
        var a = selection.Run(() => slow.Task, value => seen = value, _ => errors++, () => settled++);
        await selection.Run(() => Task.FromResult("B"), value => seen = value, _ => errors++, () => settled++);
        slow.SetException(new IOException("A slow failure")); await a;
        Assert(seen == "B" && errors == 0 && settled == 1, "actual selection coordinator ignores A slow error/finally after B success");
        foreach (var cause in new[] { "import", "new-instance", "identity-loss" })
        {
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = selection.Run(() => pending.Task, value => seen = value, _ => errors++, () => settled++);
            selection.Invalidate(); seen = cause; pending.SetResult("old"); await task;
            Assert(seen == cause && settled == 1, "selection success and finally cannot mutate newer " + cause);
        }
        var input = new ScanInputDraft(); input.SelectOwner("operation-A"); input.Text = "000A\r\n";
        Assert(input.TryBegin("command-A", out var captured) && captured!.Text == "000A" && !input.TryReceive("B") && !input.TryBegin("command-B", out _),
            "busy scanner refuses additional input/terminator explicitly without appending AB or starting another command");
        var sent = captured!;
        Assert(!input.Consume(sent, sent.CommandId, new("Conflict", "changed", 2, sent.OwnerId)) && input.Text == "000A\r\n", "failed scan retains editable original for deliberate retry");
        input.End(sent); input.TryBegin("retry-A", out var retry);
        Assert(input.Consume(retry!, "retry-A", new("Persisted", "", 3, "operation-A")) && input.Text == "", "only persisted matching scan consumes original");
        input.End(retry!); input.TryReceive("000B\t"); input.TryBegin("command-B", out var next); input.SelectOwner("operation-B"); input.TryReceive("C");
        Assert(!input.Consume(next!, next!.CommandId, new("Persisted", "", 4, "operation-A")) && input.Text == "C", "late completion cannot consume changed owner/new draft");
        var zone = TimeZoneInfo.CreateCustomTimeZone("reference UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
        var from = HistoryTimeRange.Parse(new DateTime(2026, 10, 17), "00:00", zone); var until = HistoryTimeRange.Parse(new DateTime(2026, 10, 18), "00:00", zone);
        Assert(from!.Value.UtcDateTime == new DateTime(2026, 10, 16, 16, 0, 0, DateTimeKind.Utc) && until - from == TimeSpan.FromDays(1), "local date/time converts to explicit UTC bounds preserving exclusive end");
        foreach (var time in new[] { "25:00", "12", "bogus" })
            try { HistoryTimeRange.Parse(DateTime.Today, time, zone); throw new Exception("invalid time accepted"); } catch (ArgumentException) { Assert(true, "invalid local time feedback: " + time); }
        try { HistoryTimeRange.Validate(until, from); throw new Exception("reversed range accepted"); } catch (ArgumentException) { Assert(true, "reversed range rejected before query"); }
        var wiring = File.ReadAllText(Path.Combine(workspace, "src/Ipc.Application.Supply.Desktop/ExtensionsView.xaml.cs"));
        Assert(wiring.Contains("await selection.Run") && !wiring.Contains("Draft.Build()") && wiring.Contains("selection.Invalidate()"), "actual WPF package/instance/default/import handlers use verified coordinator and partial target source; Windows event execution NOT_RUN");
    }
    static async Task IndependentReceipts(string root, string url, bool flushedFirst)
    {
        var directory = Path.Combine(root, "receipt-occurrences-" + flushedFirst); var cfg = Rig.Config(url);
        BusinessRequest original; string q1, q2; IntegrationResponse accepted;
        var faults = new FaultInjection();
        await using (var h = await Rig.Open(directory, cfg, faults))
        {
            await h.Command("OpenContext", value: "000001");
            var claim = (await h.App.ClaimAsync())!.Value;
            accepted = await h.Port.ExecuteAsync(claim.Request, CancellationToken.None);
            await h.App.UnknownAsync(claim.Request, claim.Attempt, "Synthetic lost transport response");
            original = h.Store.Request(claim.Request.Id)!;
            // Two external callers register before either original query returns.
            q1 = await h.App.BeginReceiptAsync(original, null, "original-query-Q1");
            q2 = await h.App.BeginReceiptAsync(original, null, "original-query-Q2");
            Assert(q1 != q2 && h.Store.IncompleteReceipts().Length == 2, "concurrent original queries have independent durable reservations before I/O");
            var rejected = accepted with { Result = "Rejected", Reason = "Synthetic Q1 authoritative rejection" };
            if (flushedFirst)
                Sql(h.Store.Path, "CREATE TRIGGER fail_receipt_t3 BEFORE INSERT ON responses BEGIN SELECT RAISE(ABORT,'AC15 deterministic T3 rollback'); END;");
            else faults.Hook = point => { if (point == "ReceiptCreate") throw new IOException("AC15 Q1 raw create failed without a .new file"); };
            await h.App.CompleteAsync(original, null, rejected, q1);
            if (flushedFirst)
            {
                Assert(h.Store.Responses(original.Id).Length == 0 && Directory.GetFiles(Path.Combine(directory, "receipt-ingress")).Length == 1,
                    "Q1 flushed raw survives actual SQLite T3 rollback without terminal success");
                Sql(h.Store.Path, "DROP TRIGGER fail_receipt_t3;");
                faults.Hook = point => { if (point == "ReceiptCreate") throw new IOException("AC15 Q2 raw create failed"); };
            }
            else faults.Hook = null;
            await h.App.CompleteAsync(original, null, accepted, q2);
            faults.Hook = null;
            Assert(h.Store.ReceiptOccurrence(q1)!.Status == "AwaitingResponse" &&
                h.Store.ReceiptOccurrence(q2)!.Status == (flushedFirst ? "AwaitingResponse" : "Committed"), "Q2 completion cannot erase Q1 reservation");
            Assert(h.Store.PendingRequests().Any(x => x.Id == original.Id) && ! (await h.App.SnapshotAsync()).Close.Allowed,
                "pending/recovery and business guards retain independently incomplete responsibility");
        }
        await using (var recovered = await Rig.Open(directory, cfg))
        {
            var lost = flushedFirst ? q2 : q1; var complete = flushedFirst ? q1 : q2;
            Assert(recovered.Store.ReceiptOccurrence(lost)!.Status == "RawUnavailable" && recovered.Store.ReceiptOccurrence(complete)!.Status == "Committed",
                "restart distinguishes each committed/replayed receipt from missing original custody");
            var snapshot = await recovered.App.SnapshotAsync();
            Assert(snapshot.State.Blocks.Any(x => x.Code == "ReceiptPersistenceUncertain" && x.SourceEpoch == lost) &&
                snapshot.Recovery.Any(x => x.Request.Id == original.Id) && (await recovered.App.UnknownRequestsAsync()).Any(x => x.Id == original.Id),
                "missing receipt ID remains discoverable beside original request after controlled shutdown and restart");
            var stored = recovered.Store.Request(original.Id)!;
            Assert(stored with { Status = original.Status } == original && stored.Status == (flushedFirst ? RequestStatus.Rejected : RequestStatus.Accepted) &&
                recovered.Store.AcceptedQuantity(original.SessionId) == 0 && recovered.Store.Attempts(original.Id).Length == 1,
                "terminal/payload/hash/contract/target/attempt retained and Context never counts supply acceptance");
            var details = (await recovered.App.DetailsAsync(original.Id)).Single();
            Assert(details.Receipts.Length == 2 && details.Receipts.Single(x => x.Id == lost).Response is null &&
                details.Receipts.Single(x => x.Id == complete).ContentSha256 == Wire.Hash(Wire.Encode(details.Receipts.Single(x => x.Id == complete).Response)),
                "authorized history exposes original occurrence/source/time/custody without inventing lost bytes");
            var replayed = recovered.Store.Responses(original.Id).Single();
            await recovered.App.CompleteAsync(stored, null, replayed);
            Assert(recovered.Store.IncompleteReceipts().Single().Id == lost && SupplyStore.InspectReadOnly(recovered.Store.Path).PendingRequests == 1 &&
                ! (await recovered.App.SnapshotAsync()).Admit.Allowed && await recovered.App.ClaimAsync() is null,
                "matching later delivery cannot clear another missing original; maintenance counts original once; no blind resend");
        }
    }
}
