using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

// Runs the installed product owner, real source queues/heartbeats and background sender
// against the independent process receiver. No direct business success injection.
static class ReferenceJourneyVerification
{
    public static async Task Run(string root, string url)
    {
        var dir = Path.Combine(root, "reference-journey"); var configDir = Path.Combine(dir, "config"); Directory.CreateDirectory(configDir);
        var cfg = Rig.Config(url, demand: 6); var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var definition = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, definition.ProductId, cfg.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(m => new ModuleSpec(m.Id, m.Kind, m.Version, m.ContractMajor, m.Dependencies, m.File, Wire.FileHash(Path.Combine(install, m.File)))).ToArray());
        File.WriteAllText(Path.Combine(configDir, "product.json"), Wire.Encode(manifest));
        File.WriteAllText(Path.Combine(configDir, "station.json"), Wire.Encode(cfg));
        File.WriteAllText(Path.Combine(configDir, "presentation.json"), Wire.Encode(new PresentationSettings()));
        var boot = new BootOptions(Path.Combine(configDir, "product.json"), Path.Combine(configDir, "station.json"), Path.Combine(configDir, "presentation.json"), Path.Combine(dir, "data"), install, Path.Combine(dir, "leases"));
        ProductRuntime NewRuntime() => new(boot, NullLogger<ProductRuntime>.Instance);
        List<object> evidence = [];
        async Task<StationSnapshot> Wait(ProductRuntime runtime, Func<StationSnapshot, bool> condition, string stage)
        {
            for (var i = 0; i < 200; i++)
            {
                var snapshot = await runtime.Application!.SnapshotAsync();
                if (condition(snapshot)) { evidence.Add(new { Stage = stage, snapshot.ProjectionVersion, snapshot.State.Revision, Snapshot = snapshot }); return snapshot; }
                await Task.Delay(25);
            }
            throw new TimeoutException("Reference journey: " + stage + ": " + Wire.Encode(await runtime.Application!.SnapshotAsync()));
        }
        async Task<CommandReceipt> Command(ProductRuntime runtime, string kind, string? owner = null, string? value = null)
        {
            var snapshot = await runtime.Application!.SnapshotAsync();
            var command = new Command(Wire.Id(), snapshot.State.Revision, kind, owner, value);
            var receipt = await runtime.CommandAsync(command);
            Assert(receipt.Status == "Persisted", kind + ": " + receipt.Status + ": " + receipt.Reason);
            evidence.Add(new { Stage = "command", Command = command, Receipt = receipt }); return receipt;
        }
        async Task Reset(ProductRuntime runtime, string lane)
        {
            await runtime.EmitAsync(lane, "Depart"); await Wait(runtime, s => s.State.Lanes[lane].Occupancy == Occupancy.Departed, lane + " physical-simulation-departed");
            await runtime.EmitAsync(lane, "Reset"); await Wait(runtime, s => s.State.Lanes[lane].Occupancy == Occupancy.Clear, lane + " physical-simulation-reset");
        }
        async Task Close(ProductRuntime runtime)
        {
            await Command(runtime, "CheckClose");
            var check = await Wait(runtime, s => s.State.Session?.Status == SessionStatus.AwaitingShortageDecision, "authoritative-close-check");
            await Command(runtime, "ConfirmClose", check.State.Session!.CloseCheckRequest, "CONFIRM");
            await Wait(runtime, s => s.State.Session?.Status == SessionStatus.Closed, "authoritative-box-closed");
        }
        BusinessRequest original; string boxId;
        await using (var first = NewRuntime())
        {
            await first.StartAsync(CancellationToken.None); Assert(first.Diagnostics.Count == 0, "First runtime starts");
            await Wait(first, s => s.State.Devices.Values.All(x => x.IsValid(DateTimeOffset.UtcNow)) && s.State.IntegrationHealth?.IsValid(DateTimeOffset.UtcNow) == true, "startup-source-health-ready");
            await Command(first, "Login", value: "maintenance");
            await Command(first, "OpenContext", value: "000001");
            boxId = (await Wait(first, s => s.State.Session?.Status == SessionStatus.Active, "box-validated")).State.Session!.Id;
            await first.EmitAsync("left", "Enter"); await first.EmitAsync("right", "Enter");
            var both = await Wait(first, s => s.State.Lanes.Count == 2, "dual-channel-observation");
            var left = both.State.Lanes["left"].Id; var right = both.State.Lanes["right"].Id;
            await Command(first, "SetFixtureFault", value: "Reject");
            await first.EmitAsync("left", "Read", ["000123"], 2);
            await Wait(first, s => s.State.Lanes["left"].Status == OperationStatus.ValidationRejected, "business-rejection-retained");
            await first.EmitAsync("left", "Read", ["000123"], 2);
            await Wait(first, s => s.State.Lanes["left"].Status == OperationStatus.AwaitingAction, "corrected-input-validated");
            await Command(first, "Submit", left);
            await Wait(first, s => s.State.Lanes["left"].Status == OperationStatus.Accepted && s.AcceptedQuantity == 2, "first-unique-acceptance");
            await Reset(first, "left");
            await first.EmitAsync("right", "Read", ["000456"], 8);
            var split = await Wait(first, s => s.State.Lanes["right"].Status == OperationStatus.SplitRequired, "split-current-quantity-authority");
            Assert(split.State.Lanes["right"].Supply == 4, "8/4/4 reference split quantity");
            for (var i = 0; i < 4; i++) await Command(first, "ScanRetained", right, "unit-" + i);
            await Command(first, "SetFixtureFault", value: "Drop");
            await Command(first, "Submit", right);
            var unknown = await Wait(first, s => s.State.Lanes["right"].Status == OperationStatus.OutcomeUnknown, "lost-receipt-unknown-no-resend");
            original = unknown.Requests.Single(x => x.Status == RequestStatus.Unknown);
            Assert(!unknown.Admit.Allowed && unknown.AcceptedQuantity == 2, "Unknown effect blocks admission without inventing local acceptance");
            await Task.Delay(300);
            var retained = (await first.Application!.DetailsAsync(original.Id)).Single();
            Assert(retained.Request.Payload == original.Payload && retained.Attempts.Length == 1, "Original request bytes/binding and single attempt retained before restart");
            evidence.Add(new { Stage = "original-request-before-restart", Detail = retained });
        }
        await using (var second = NewRuntime())
        {
            await second.StartAsync(CancellationToken.None); Assert(second.Diagnostics.Count == 0, "Controlled restart succeeds");
            var restored = await second.Application!.SnapshotAsync();
            Assert(restored.State.Actor is null && restored.State.Session?.Id == boxId && restored.Requests.Single(x => x.Id == original.Id).Status == RequestStatus.Unknown, "Identity reset and same original Unknown after restart");
            await Wait(second, s => s.State.IntegrationHealth?.IsValid(DateTimeOffset.UtcNow) == true, "restart-source-health-confirmed");
            await Command(second, "Login", value: "maintenance");
            await second.ReconcileAsync(original.Id);
            await Wait(second, s => s.State.Lanes["right"].Status == OperationStatus.Accepted && s.AcceptedQuantity == 6, "original-authoritative-query-unique-acceptance");
            await second.ReconcileAsync(original.Id).ContinueWith(t => { Assert(t.IsFaulted && t.Exception!.GetBaseException() is GuardException, "Resolved request cannot be reconciled as Unknown again"); });
            var detail = (await second.Application!.DetailsAsync(original.Id)).Single();
            Assert(detail.Request.Payload == original.Payload && detail.Request.PayloadHash == original.PayloadHash && detail.Request.Binding == original.Binding && detail.Attempts.Length == 1, "No new request/attempt/target/contract after restart/reconciliation");
            evidence.Add(new { Stage = "original-request-after-reconciliation", Detail = detail });
            await Reset(second, "right"); await Close(second);
            await Command(second, "OpenContext", value: "000002");
            await Wait(second, s => s.State.Session?.Status == SessionStatus.Active && s.AcceptedQuantity == 0, "new-box-counter-scope");
            var history = await second.Application.QueryHistoryAsync(new(SessionId: boxId));
            Assert(history.Items.Single().Session.Status == SessionStatus.Closed && history.Items.Single().AcceptedQuantity == 6 && history.Items.Single().OperationCount == 2, "Old box history remains complete after new box");
            await Close(second);
        }
        await using (var final = NewRuntime())
        {
            await final.StartAsync(CancellationToken.None); Assert(final.Diagnostics.Count == 0, "Second controlled restart succeeds");
            await Wait(final, s => s.State.IntegrationHealth?.IsValid(DateTimeOffset.UtcNow) == true, "final-restart-source-health-confirmed");
            await Command(final, "Login", value: "maintenance");
            var history = await final.Application!.QueryHistoryAsync(new());
            Assert(history.TotalCount == 2 && history.Items.All(x => x.Session.Status == SessionStatus.Closed), "Both closed boxes remain queryable after restart");
            evidence.Add(new { Stage = "sustained-two-box-history-after-restart", History = history });
        }
        using (var receiver = new SqliteConnection($"Data Source={Path.Combine(root, "receiver", "receiver.db")};Mode=ReadOnly;Pooling=False"))
        {
            receiver.Open(); using var query = receiver.CreateCommand(); query.CommandText = "SELECT count(*) FROM network_calls WHERE id=$id"; query.Parameters.AddWithValue("$id", original.Id);
            Assert(Convert.ToInt32(query.ExecuteScalar()) == 1, "Independent receiver observed exactly one send of original lost-response request");
        }
        File.WriteAllText(Path.Combine(dir, "journey.json"), Wire.Encode(new { Mode = "SIMULATION", Policy = cfg.Policy, cfg.StationId, OriginalRequestId = original.Id, Evidence = evidence, Result = "PASS", Windows = "NOT_RUN", Production = "BLOCKED" }));
        Console.WriteLine("PASS REFERENCE-JOURNEY startup/identity/box/dual-lane/rejection/split/acceptance/Unknown/original-query/physical-simulation-reset/close/new-box/history/restart");
    }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException("REFERENCE-JOURNEY: " + reason); }
}
