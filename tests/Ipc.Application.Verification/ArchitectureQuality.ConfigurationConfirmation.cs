using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;
using System.Text;

static partial class ArchitectureQuality
{
    public static async Task ConfigurationConfirmationChecks(string source, string evidence)
    {
        workspace = source; root = evidence; failures = 0;
        await StartupRoutingChecks();
        var c = Config("sportmaster");
        async Task<BusinessReceipt> ConfirmOnce(ParcelRuntime app, string owner, string reason)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var snapshot = await app.SnapshotAsync();
                if (snapshot.Availability?.Configure != true) { await Task.Delay(20); continue; }
                var result = await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "ConfirmConfiguration", owner, reason));
                if (result.Status != "Rejected" || !(result.Reason.Contains("guard is closed") || result.Reason.Contains("resource health"))) return result;
                await Task.Delay(20);
            }
            throw new Exception("Confirmation resource guards did not settle");
        }
        string Authority(string name) => Path.Combine(DirectoryFor(name, c), "configuration.json");
        async Task<(ParcelConfiguration Effective, byte[] Bytes)> Legacy(string name)
        {
            ParcelConfiguration effective;
            await using (var initial = await Open(c, name)) effective = (await initial.SnapshotAsync()).Configuration.Effective with { TimeoutMs = c.TimeoutMs + 31 };
            // Historical format has no resource activation/confirmation evidence. Preserve its exact BOM and bytes.
            File.WriteAllText(Authority(name), Wire.Encode(new ConfigurationRevision<ParcelConfiguration>(17, effective, null, "Effective", null)), new UTF8Encoding(true));
            return (effective, File.ReadAllBytes(Authority(name)));
        }
        var legacy = await Legacy("confirmation-legacy");
        await using (var app = await Open(c, "confirmation-legacy"))
        {
            var s = await app.SnapshotAsync(); var confirmation = s.Configuration.Confirmation!;
            Check(s.Paused && !s.Availability!.Resume && confirmation is { Required: true, State: "AwaitingMaintenance", Source: "LegacyEnvelope" } &&
                s.Configuration.Status == "ConfirmationRequired" && s.Configuration.Effective.TimeoutMs == legacy.Effective.TimeoutMs,
                "legacy Effective successful startup remains explicitly unconfirmed and paused; seed does not replace old value");
            Check(Convert.FromBase64String(confirmation.OriginalEnvelopeBase64!).SequenceEqual(legacy.Bytes) && confirmation.OriginalEnvelopeSha256 ==
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(legacy.Bytes)), "legacy envelope original bytes including BOM and SHA256 retained in the authoritative envelope");
            var owner = s.RuntimeId.ToString("N") + ":" + s.Configuration.Revision + ":" + confirmation.EffectiveHash;
            await app.ExitManagementModeAsync();
            Check((await Command(app, "ConfirmConfiguration", owner, "Unauthenticated attempt")).Status == "Rejected", "legacy confirmation requires maintenance grant");
            await Maintenance(app);
            Check((await Command(app, "Resume")).Status == "Rejected", "software Resume cannot bypass unconfirmed Effective");
            Check((await ConfirmOnce(app, s.RuntimeId.ToString("N") + ":17:" + confirmation.EffectiveHash, "Stale configuration window")).Status == "Conflict", "legacy confirmation binds current configuration CAS");
            Check((await ConfirmOnce(app, s.RuntimeId.ToString("N") + ":" + s.Configuration.Revision + ":" + new string('0', 64), "Wrong inspected content")).Status == "Conflict", "legacy confirmation binds inspected Effective hash");
            Check((await ConfirmOnce(app, Guid.NewGuid().ToString("N") + ":" + s.Configuration.Revision + ":" + confirmation.EffectiveHash, "Old application instance")).Status == "Conflict", "legacy confirmation binds the inspected runtime identity at the business guard");
            var stale = await app.SnapshotAsync(); await Command(app, "Pause");
            Check((await app.CommandAsync(new(Wire.Id(), stale.Revision, "ConfirmConfiguration", owner, "Stale application projection"))).Status == "Conflict", "legacy confirmation also binds application CAS");
            var confirmed = await Command(app, "ConfirmConfiguration", owner, "Operator inspected preserved original and current healthy resources");
            if(confirmed.Status == "Rejected" && confirmed.Reason.Contains("resource health")) { Console.WriteLine("WAIT resource confirmation: " + confirmed.Reason); confirmed = await ConfirmOnce(app,owner,"Operator inspected preserved original and current healthy resources"); }
            Check(confirmed.Status == "Persisted", "management mode confirms current revision and content with reason: " + confirmed.Status + " " + confirmed.Reason);
            s = await app.SnapshotAsync();
            Check(s.Paused && s.Configuration.Confirmation is { Required: false, State: "Confirmed" } && s.Configuration.Confirmation.ConfirmedBy == LocalManagementMode.ForRoot(Path.Combine(root,"confirmation-legacy","data")).Evidence?.AuditActor && s.Configuration.Status == "Effective",
                "confirmation records identity but keeps software pause for explicit Resume");
            Check((await Command(app, "Resume")).Status == "Persisted", "confirmed Effective can resume through ordinary guards");
        }
        await using (var reopened = await Open(c, "confirmation-legacy"))
            Check((await reopened.SnapshotAsync()).Configuration.Confirmation is { Required: false, State: "Confirmed" }, "confirmed legacy status survives restart without a new approval");
        var priorConfirmed = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority("confirmation-legacy")));
        try { await using var ignored = await Open(c, "confirmation-legacy", f => new FailingConfirmationFactory(f)); Check(false, "ordinary confirmed restart failure expected"); }
        catch (InvalidOperationException ex) when (ex.Message == "Synthetic Create failure") { }
        var confirmedFailure = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority("confirmation-legacy")));
        Check(confirmedFailure.Confirmation == priorConfirmed.Confirmation || Wire.Encode(confirmedFailure.Confirmation) == Wire.Encode(priorConfirmed.Confirmation),
            "failure on ordinary confirmed restart does not invent unknown prior Effective or erase confirmation");

        var failedLegacy = await Legacy("confirmation-failed");
        try { await using var ignored = await Open(c, "confirmation-failed", f => new FailingConfirmationFactory(f)); Check(false, "legacy failed startup expected"); }
        catch (InvalidOperationException ex) when (ex.Message == "Synthetic Create failure") { }
        var failed = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority("confirmation-failed")));
        Check(failed is { Status: "ConfirmationRequired", Confirmation.Required: true } && failed.Failure!.Contains("Synthetic Create failure") &&
            failed.Confirmation.Attempts is [{ Outcome: "Failed", FinishedAt: not null }] && failed.Effective.TimeoutMs == failedLegacy.Effective.TimeoutMs,
            "legacy Create failure persists original Effective and failed startup attempt without claiming activated");
        await using (var restarted = await Open(c, "confirmation-failed"))
        {
            var s = await restarted.SnapshotAsync(); var confirmation = s.Configuration.Confirmation!;
            Check(s.Paused && confirmation is { Required: true, State: "AwaitingMaintenance" } && confirmation.Attempts.Select(a => a.Outcome).SequenceEqual(new[] { "Failed", "ResourcesStarted" }),
                "legacy failure restart retains both failed and successful resource attempts and still requires maintenance");
            await Maintenance(restarted);
            Check((await restarted.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { TimeoutMs = c.TimeoutMs + 47 }, "Candidate staged while legacy confirmation pending")).Status == "PendingRestart", "legacy confirmation does not erase independent Pending candidate");
        }
        await using (var restarted = await Open(c, "confirmation-failed"))
        {
            var s = await restarted.SnapshotAsync();
            Check(s.Configuration.Effective.TimeoutMs == failedLegacy.Effective.TimeoutMs && s.Configuration.Pending?.TimeoutMs == c.TimeoutMs + 47 &&
                s.Configuration.Confirmation is { Required: true } && Convert.FromBase64String(s.Configuration.Confirmation.OriginalEnvelopeBase64!).SequenceEqual(failedLegacy.Bytes),
                "Stage and restart retain exact legacy envelope and Pending without activating an unconfirmed base");
            await Maintenance(restarted);
            Check((await ConfirmOnce(restarted, s.RuntimeId.ToString("N") + ":" + s.Configuration.Revision + ":" + s.Configuration.Confirmation!.EffectiveHash, "Inspect legacy before candidate activation")).Status == "Persisted", "maintenance can confirm legacy base while preserving candidate");
        }
        await using (var activated = await Open(c, "confirmation-failed"))
        {
            var s = await activated.SnapshotAsync();
            Check(s.Configuration.Effective.TimeoutMs == c.TimeoutMs + 47 && s.Configuration.Pending is null && s.Configuration.Activation?.Outcome == "Activated" && s.Configuration.Confirmation?.Required == false,
                "candidate activates only after legacy confirmation plus a subsequent successful resource startup");
        }

        await Legacy("confirmation-interrupted");
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(typeof(ArchitectureQuality).Assembly.Location); psi.ArgumentList.Add(workspace);
        psi.ArgumentList.Add("--configuration-confirmation-crash-child"); psi.ArgumentList.Add(Path.Combine(root, "confirmation-interrupted"));
        using (var child = Process.Start(psi)!)
        {
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Console.Write(await stdout); Console.Write(await stderr);
            var interrupted = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority("confirmation-interrupted")));
            Check(child.ExitCode == 73 && interrupted.Confirmation is { Required: true, State: "InProgress" } && interrupted.Confirmation.Attempts is [{ FinishedAt: null }],
                "real child process termination before Create leaves durable unfinished legacy resource attempt");
        }
        await using (var recovered = await Open(c, "confirmation-interrupted"))
        {
            var s = await recovered.SnapshotAsync();
            Check(s.Paused && s.Configuration.Confirmation is { Required: true, State: "AwaitingMaintenance" } && s.Configuration.Confirmation.Attempts.Select(a => a.Outcome).SequenceEqual(new[] { "Interrupted", "ResourcesStarted" }),
                "restart records interrupted legacy attempt and requires maintenance even after resources subsequently start");
        }
        try { await using var ignored = await Open(c, "confirmation-seed-failed", f => new FailingConfirmationFactory(f)); Check(false, "seed failed startup expected"); }
        catch (InvalidOperationException ex) when (ex.Message == "Synthetic Create failure") { }
        var seedFailed = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Authority("confirmation-seed-failed")));
        Check(seedFailed.Confirmation is { Required: true, Source: "Seed" } && seedFailed.Confirmation.Attempts is [{ Outcome: "Failed" }], "new seed is not effective after resource Create failure");
        await using (var seedRecovered = await Open(c, "confirmation-seed-failed"))
            Check((await seedRecovered.SnapshotAsync()).Configuration.Confirmation is { Required: false, Source: "Seed", State: "Confirmed" }, "new seed can confirm automatically only after actual resource startup succeeds");
        var activatedLegacy = await Legacy("confirmation-prior-activated");
        ExtensionStore.AtomicWrite(Authority("confirmation-prior-activated"), new ConfigurationRevision<ParcelConfiguration>(19, activatedLegacy.Effective, null, "Effective", null,
            new("prior-success", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, "Activated")));
        await using (var priorActivated = await Open(c, "confirmation-prior-activated"))
            Check((await priorActivated.SnapshotAsync()).Configuration.Confirmation is { Required: false, Source: "PriorActivation", State: "Confirmed", Attempts.Length: 0 },
                "historical successful activation is sufficient evidence; regular restart does not require new maintenance approval");
        var vmLegacy = await Legacy("confirmation-vm-cas");
        await using (var app = await Open(c, "confirmation-vm-cas"))
        {
            await Maintenance(app);
            var firstWindow = new ParcelViewModel(app, "Maintenance A", "Sportmaster");
            var secondWindow = new ParcelViewModel(app, "Maintenance B", "Sportmaster");
            await firstWindow.LoadConfigurationConfirmationAsync(); await secondWindow.LoadConfigurationConfirmationAsync();
            firstWindow.LoadConfigurationDraft(); secondWindow.LoadConfigurationDraft();
            var originalEvidence = firstWindow.ConfigurationConfirmationEvidence;
            firstWindow.ConfigurationDraft = Wire.Encode(vmLegacy.Effective with { TimeoutMs = c.TimeoutMs + 103 });
            var unsavedDraft = firstWindow.ConfigurationDraft;
            firstWindow.ConfigurationConfirmationReason = "Window A inspected original before concurrent configuration change";
            secondWindow.ConfigurationDraft = Wire.Encode(secondWindow.Snapshot!.Configuration.Effective with { TimeoutMs = c.TimeoutMs + 109 });
            secondWindow.ConfigurationReason = "Window B changed saved configuration revision";
            await secondWindow.SaveConfigurationAsync(); await firstWindow.RefreshAsync();
            Check(firstWindow.EffectiveConfigurationHeading.Contains("尚待确认") && firstWindow.ConfigurationConfirmationEvidence == originalEvidence &&
                firstWindow.ConfigurationConfirmationState.Contains("修订已改变") && firstWindow.ConfigurationDraft == unsavedDraft,
                "real confirmation VM refresh keeps read-time identity, original evidence and unsaved draft; unconfirmed input is not labelled confirmed Effective");
            await firstWindow.RunAsync(firstWindow.ConfirmConfigurationAsync);
            Check(firstWindow.Message.StartsWith("Conflict") && (await app.SnapshotAsync()).Configuration.Confirmation?.Required == true &&
                firstWindow.ConfigurationConfirmationEvidence == originalEvidence && firstWindow.ConfigurationDraft == unsavedDraft &&
                firstWindow.ConfigurationConfirmationReason == "Window A inspected original before concurrent configuration change",
                "two real maintenance VMs reject stale confirmation after refresh without advancing Owner or erasing evidence, reason or draft");
            await Wait(app, state => state.Availability!.Configure); await firstWindow.RefreshAsync();
            await firstWindow.LoadConfigurationConfirmationAsync();
            for (var retry = 0; retry < 100 && !firstWindow.CanConfirmConfiguration; retry++) { await Task.Delay(20); await firstWindow.RefreshAsync(); }
            Check(firstWindow.ConfigurationConfirmationEvidence != originalEvidence && firstWindow.ConfigurationDraft == unsavedDraft && firstWindow.CanConfirmConfiguration,
                "explicit re-read updates confirmation identity while keeping independent edit draft");
            await firstWindow.RunAsync(firstWindow.ConfirmConfigurationAsync);
            for (var attempt = 0; attempt < 100 && firstWindow.Message.StartsWith("Rejected") && (firstWindow.Message.Contains("guard is closed") || firstWindow.Message.Contains("resource health")); attempt++)
            { await Task.Delay(20); await firstWindow.RefreshAsync(); await firstWindow.RunAsync(firstWindow.ConfirmConfigurationAsync); }
            var confirmed = await app.SnapshotAsync();
            Check(firstWindow.Message.StartsWith("Persisted") && confirmed.Paused && confirmed.Configuration.Confirmation?.Required == false &&
                firstWindow.EffectiveConfigurationHeading == "最后确认 Effective" && confirmed.Configuration.Pending?.TimeoutMs == c.TimeoutMs + 109,
                "WPF confirmation action calls the shared business guard and preserves pause plus other window Pending");
        }
        if (failures != 0) throw new Exception("Configuration confirmation failures: " + failures);
        Console.WriteLine("PASS CONFIGURATION CONFIRMATION COMPLETE");
    }
    sealed class FailingConfirmationFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) => throw new InvalidOperationException("Synthetic Create failure");
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
