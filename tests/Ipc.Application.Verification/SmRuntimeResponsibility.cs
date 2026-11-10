using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

internal static class SmRuntimeResponsibility
{
    static void Check(bool ok, string detail)
    {
        if (!ok) throw new InvalidOperationException(detail);
        Console.WriteLine("PASS SM RESPONSIBILITY " + detail);
    }

    public static async Task Run(string workspace, string root)
    {
        await using var station = new ManualSupplyHarness(workspace, Path.Combine(root, "sm-runtime-responsibility"), deferred: true, maximumOperations: 1);
        var seed = station.Config with
        {
            Wcs = station.Config.Wcs! with { Parameters = new(station.Config.Wcs.Parameters) { ["request-timeout-ms"] = "1500" } }
        };
        ExtensionStore.AtomicWrite(Path.Combine(station.Launch.ConfigurationDirectory, "business.json"), seed);
        var password = "Synthetic-" + Wire.Id();
        var flow = new SportmasterFlow();
        var localManagement = LocalManagementMode.ForRoot(station.Launch.DataRoot);
        localManagement.ConfigurePassword(password);
        localManagement.Exit();

        ParcelRuntime NewRuntime() => new(station.Launch, flow,
            new ParcelCapabilityFactory(station.Launch, BusinessCompatibility.Read(typeof(SportmasterFlow).Assembly), typeof(SportmasterFlow).Assembly)
            {
                RequireProductionManagement = root =>
                {
                    if (root != station.Launch.DataRoot || !localManagement.Status().Configured)
                        throw new InvalidOperationException("Only this isolated fixture has a local authority");
                }
            })
        {
            // The fixture has its own data root and loopback peer. Only the
            // installed-machine preflight is outside this application test.
            RuntimeIdentityPreflight = () => { }
        };

        async Task Authorize(ParcelRuntime app)
        {
            if (!(await app.ManagementModeStatusAsync()).Configured) await app.ConfigureManagementPasswordAsync(password);
            if (!await app.EnterManagementModeAsync(password)) throw new InvalidOperationException("Synthetic management grant rejected");
        }

        static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> done)
        {
            ParcelSnapshot? snapshot = null;
            await CameraRoundHarness.Until(async () => done(snapshot = await app.SnapshotAsync()));
            return snapshot!;
        }

        static async Task<ParcelBarcodeReceipt> Submit(ParcelRuntime app, string position, string code)
        {
            var s = await app.SnapshotAsync();
            var command = new SubmitParcelBarcode(Wire.Id(), s.RuntimeId, s.Configuration.Revision, position, code, ParcelBarcodeInputKind.Keyboard,
                s.Inputs?.SingleOrDefault(i => i.PositionId == position)?.Observation.Id, s.Positions!.Single(p => p.Id == position).CurrentOperation);
            return await app.SubmitParcelBarcodeAsync(command, default);
        }

        static async Task Remove(ParcelRuntime app, ParcelOperation op)
        {
            var s = await app.SnapshotAsync();
            var receipt = await app.CommandAsync(new(Wire.Id(), s.Revision, "ConfirmPlacementRemoval", op.PositionId, op.Id));
            Check(receipt.Status == "Persisted", "explicit removal confirms the identified original placement");
        }

        const string oldBarcode = "HIST-HELD";
        const string pattern = "^VALID-[0-9]+$";
        station.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        station.Peer.HoldWhen = body => body.Contains("HIST-", StringComparison.Ordinal);
        string originalId, originalRequest;
        await using (var app = NewRuntime())
        {
            await app.StartAsync(default);
            var ready = await Wait(app, s => s.Positions!.All(p => p.BarcodeInput?.Manual == true));
            var candidate = seed with { BarcodeFilter = new(pattern) };
            var unauthorized = await app.StageConfigurationAsync(ready.Configuration.Revision, candidate, "Synthetic unauthorized rule change");
            Check(unauthorized.Status == "Rejected" && (await app.SnapshotAsync()).Configuration.Revision == ready.Configuration.Revision, "Production configuration still requires a management grant");
            await Authorize(app);

            var submit = Submit(app, "left", oldBarcode);
            await CameraRoundHarness.Until(() => Task.FromResult(station.Peer.Requests.Any(body => body.Contains(oldBarcode))));
            var active = await Wait(app, s => s.Operations.Any(op => op.Wcs?.State == StageState.Dispatching));
            Check(active.Capacity is { Unresolved: 1, AdmissionBlock: ParcelInputBlockReason.CapacityExhausted } &&
                !active.Positions!.Single(p => p.Id == "right").Admit, "a real in-flight request consumes the last slot and fences the other position");
            var saving = app.StageConfigurationAsync(active.Configuration.Revision, candidate with { TimeoutMs = seed.TimeoutMs + 1 }, "Synthetic rule and station setting");
            Check(!saving.IsCompleted, "an in-flight external call fences activation while the save waits for quiescence");
            await submit;
            var saved = await saving;
            var unresolved = await Wait(app, s => s.Operations.Any(op => op.Wcs?.State == StageState.Unknown));
            var original = unresolved.Operations.Single(op => op.ParcelCode == oldBarcode);
            originalId = original.Id;
            originalRequest = Wire.Encode(original.Wcs!.Request);
            Check(original.Wcs.Request.IntegrationRequest?.Body.Length > 0, "the original Production request has its frozen HTTP body");
            var pending = await app.SnapshotAsync();
            Check(saved.Status == "PendingRestart" && pending.ConfigurationActivationBlock == ParcelInputBlockReason.PreviousResultUnconfirmed &&
                pending.Configuration.Effective.BarcodeFilter?.Pattern != pattern && pending.Capacity?.HistoricalUnknown == 0,
                "a timeout does not prove departure or activate new configuration: " + saved.Status + " / " + saved.Reason);
        }

        await using (var app = NewRuntime())
        {
            await app.StartAsync(default);
            await Authorize(app);
            var pending = await app.SnapshotAsync();
            Check(pending.Configuration.Status == "PendingRestart" && pending.ConfigurationActivationBlock == ParcelInputBlockReason.PreviousResultUnconfirmed &&
                pending.Positions!.Single(p => p.Id == "left").CurrentOperation == originalId,
                "restart retains pending settings and the actual unresolved placement");
            await Remove(app, pending.Operations.Single(op => op.Id == originalId));
            var cleared = await Wait(app, s => s.Capacity?.HistoricalUnknown == 1);
            Check(cleared.ConfigurationActivationBlock is null && cleared.Capacity!.Unresolved == 0 && cleared.Operations.All(op => op.Id != originalId),
                "confirmed departure removes execution ownership and frees capacity while preserving pending activation");
        }

        await using (var app = NewRuntime())
        {
            await app.StartAsync(default);
            await Authorize(app);
            var activated = await Wait(app, s => s.Positions!.All(p => p.BarcodeInput?.Manual == true));
            Check(activated.Configuration.Pending is null && activated.Configuration.Effective.BarcodeFilter?.Pattern == pattern &&
                activated.Capacity is { HistoricalUnknown: 1, Unresolved: 0 } && activated.Operations.All(op => op.Id != originalId),
                "pending settings activate after restart and the historical original is not reloaded");
            Check(await app.OperationAsync(originalId) is { Wcs: { } frozen } && Wire.Encode(frozen.Request) == originalRequest,
                "record lookup preserves the complete frozen request across activation");
            var revision = activated.Configuration.Revision;
            var rules = activated.Configuration.Effective with { BarcodeFilter = new("^(HIST|VALID)-[0-9]+$") };
            var saved = await app.StageConfigurationAsync(revision, rules, "Synthetic live barcode rules");
            Check(saved.Status == "Effective", "Production rules save live with historical Unknown present");

            for (var i = 1; i <= 3; i++)
            {
                var code = "HIST-" + i;
                await Submit(app, "left", code);
                var unknown = await Wait(app, s => s.Operations.Any(op => op.ParcelCode == code && op.Wcs?.State == StageState.Unknown));
                await Remove(app, unknown.Operations.Single(op => op.ParcelCode == code));
                var cleared = await Wait(app, s => s.Capacity?.HistoricalUnknown == i + 1);
                Check(cleared.Capacity is { Unresolved: 0, Resident: 0, Available: true }, "history above the one-slot budget leaves no resident execution");
            }

            station.Peer.ReplySlot = "CURRENT-CHUTE";
            await Submit(app, "right", "VALID-101");
            var accepted = await Wait(app, s => s.Operations.Any(op => op.ParcelCode == "VALID-101" && op.Slot == "CURRENT-CHUTE"));
            var currentId = accepted.Positions!.Single(p => p.Id == "right").CurrentOperation;
            Check(currentId is not null && accepted.Operations.Single(op => op.Id == currentId).ParcelCode == "VALID-101" &&
                station.Peer.Requests.Count(body => body.Contains("VALID-101")) == 1, "the valid new parcel sends once and owns the displayed chute");

            var priorSends = station.Peer.Requests.Count;
            await Submit(app, "left", "REJECTED-102");
            var rejected = await Wait(app, s => s.Inputs?.Any(input => input.PositionId == "left" && input.Outcome == ParcelInputOutcome.Rejected) == true);
            Check(rejected.Inputs!.Single(input => input.PositionId == "left").BarcodeSelection?.Outcome == "NoMatchingCode" &&
                station.Peer.Requests.Count == priorSends, "a barcode rejected by the effective rules never reaches RCS");

            station.Peer.Hold.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(station.Peer.BusinessRepliesAttempted >= priorSends));
            var after = await app.SnapshotAsync();
            Check(after.Positions!.Single(p => p.Id == "right").CurrentOperation == currentId &&
                after.Operations.Single(op => op.Id == currentId).Slot == "CURRENT-CHUTE" &&
                station.Peer.Requests.Count == priorSends && station.Peer.Requests.Count(body => body.Contains(oldBarcode)) == 1,
                "late HTTP replies do not cross onto the current parcel or replay any original");
            var records = (await app.HistoryAsync()).Items.Where(item => item.Operation.Wcs?.State == StageState.Unknown).ToArray();
            Check(records.Length == 4 && records.All(item => item.Archived) &&
                Wire.Encode(records.Single(item => item.Operation.Id == originalId).Operation.Wcs!.Request) == originalRequest,
                "all unknown results and original payloads remain available on demand");
        }

        await using (var app = NewRuntime())
        {
            await app.StartAsync(default);
            var snapshot = await Wait(app, s => s.Positions!.All(p => p.BarcodeInput?.Manual == true));
            Check(snapshot.Capacity is { HistoricalUnknown: 4, Unresolved: 0 } &&
                snapshot.Operations.All(op => op.Wcs?.State != StageState.Unknown) &&
                station.Peer.Requests.Count == 5, "restart after multiple historical results preserves classification and sends nothing");

            await Authorize(app);
            app.Boundary = point => { if (point == "AfterRawDurable") throw new IOException("Synthetic receipt commit interruption"); };
            try { await Submit(app, "left", "VALID-103"); }
            catch (IOException error) when (error.Message == "Synthetic receipt commit interruption") { }
            await app.FatalFailure.WaitAsync(TimeSpan.FromSeconds(5));
            snapshot = await app.SnapshotAsync();
            var refused = await app.StageConfigurationAsync(snapshot.Configuration.Revision,
                snapshot.Configuration.Effective with { BarcodeFilter = new("^OTHER$") }, "Synthetic blocked write");
            Check(refused.Status == "Rejected" && snapshot.Capacity?.AdmissionBlock == ParcelInputBlockReason.StorageUnavailable &&
                snapshot.Capacity.HistoricalUnknown == 4 && snapshot.Operations.Any(op => op.ParcelCode == "VALID-103"),
                "an uncommitted durable reply retains its owner and storage failure blocks configuration");
        }

        await using (var app = NewRuntime())
        {
            await app.StartAsync(default);
            var snapshot = await Wait(app, s => s.Capacity?.HistoricalUnknown == 4 && s.Positions!.All(p => p.BarcodeInput?.Manual == true));
            Check(snapshot.Capacity?.HistoricalUnknown == 4 && (await app.HistoryAsync()).Items.Any(item => item.Operation is { ParcelCode: "VALID-103", Slot: "CURRENT-CHUTE" }) && station.Peer.Requests.Count == 6,
                "restart commits the original durable reply without resending or recategorizing it as historical Unknown");
        }
    }
}
