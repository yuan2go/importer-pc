using System.Security.Principal;
using System.Text.Json.Nodes;
using Ipc.Assembly;
using Ipc.Platform;

public static class DeploymentBatchVerification
{
    public static void Run(DeploymentMedia media, string evidence)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (Directory.Exists(evidence) || File.Exists(evidence)) throw new IOException("Use a new evidence directory");
        Directory.CreateDirectory(evidence);
        var cases = new List<string>();
        var seed = Path.Combine(evidence, "seed"); Directory.CreateDirectory(seed);
        foreach (var source in WindowsDeploymentSetup.ConfigurationFiles(Path.Combine(media.Root, "app", "config")))
            File.Copy(source, Path.Combine(seed, Path.GetFileName(source)), false);
        File.WriteAllText(Path.Combine(seed, "review-extra.json"), "{\"Synthetic\":true}");
        using var identity = WindowsIdentity.GetCurrent();
        var runtimeSid = identity.User!.Value;
        var data = Path.Combine(evidence, "never-installed-data");
        var original = ParcelStartupServices.Review(media, runtimeSid, data, seed, null, [], false)
            with { Shortcuts = new(false, false) };
        var plan = DeploymentBatch.Freeze(original);
        var planFile = Path.Combine(evidence, "plan.json"); File.WriteAllText(planFile, Wire.Encode(plan));
        var planHash = Wire.FileHash(planFile);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        JsonObject Invoke(string name, string file, int exit, bool preflight = true)
        {
            var receipt = Path.Combine(evidence, name + ".json");
            var args = new List<string> { "--deployment-plan", file, "--receipt", receipt };
            if (preflight) args.Add("--preflight");
            Check(DeploymentBatch.Handle(args.ToArray()), name + ": entry not handled");
            var actual = Environment.ExitCode; Environment.ExitCode = 0;
            Check(actual == exit, name + ": wrong exit " + actual + "; " + File.ReadAllText(receipt));
            Check(!Directory.Exists(data), name + ": deployment data was created");
            return JsonNode.Parse(File.ReadAllText(receipt))!.AsObject();
        }
        string Save(string name, WindowsDeploymentRequest request)
        { var file = Path.Combine(evidence, name + ".plan.json"); File.WriteAllText(file, Wire.Encode(DeploymentBatch.Freeze(request))); return file; }
        string RejectedPlan(string name, WindowsDeploymentRequest request)
        {
            var bytes = Wire.Encode(request); var refused = false;
            try { _ = DeploymentBatch.Freeze(request); } catch (InvalidDataException) { refused = true; }
            Check(refused && Wire.Encode(request) == bytes, name + ": invalid plan was frozen or changed");
            refused = false;
            try { _ = ParcelStartupServices.RevalidateDeployment(media, request); } catch (InvalidDataException) { refused = true; }
            Check(refused && Wire.Encode(request) == bytes, name + ": invalid plan was revalidated or changed");
            // Serialize adversarial external input directly, without making the
            // current writer manufacture an unsupported plan for the test.
            var file = Path.Combine(evidence, name + ".plan.json");
            File.WriteAllText(file, Wire.Encode(new WindowsDeploymentPlan(1, Wire.Hash(bytes), request)));
            return file;
        }

        for (var i = 0; i < 3; i++)
        {
            var checkedRequest = ParcelStartupServices.RevalidateDeployment(media, original);
            Check(checkedRequest.OperationId == original.OperationId && Wire.Hash(Wire.Encode(checkedRequest)) == plan.PlanSha256, "Re-review changed original identity");
            var result = Invoke("preflight-" + i, planFile, 0);
            Check(result["Status"]!.GetValue<string>() == "PreflightPassed" && result["PlanSha256"]!.GetValue<string>() == plan.PlanSha256, "Preflight changed plan identity");
        }
        Check(Wire.FileHash(planFile) == planHash, "Re-preflight rewrote original plan"); cases.Add("same-plan-three-reviews-and-preflights-stable");
        var ordered = original with { ConfigurationHashes = original.ConfigurationHashes!.Reverse().ToDictionary(p => p.Key, p => p.Value) };
        var orderedHash = Wire.Hash(Wire.Encode(ordered));
        Check(Wire.Hash(Wire.Encode(ParcelStartupServices.RevalidateDeployment(media, ordered))) == orderedHash, "Re-review rehashed dictionary ordering");
        Check(Invoke("ordered-preflight", Save("ordered", ordered), 0)["PlanSha256"]!.GetValue<string>() == orderedHash, "Dictionary ordering changed the frozen plan");
        cases.Add("full-semantic-comparison-preserves-frozen-dictionary-order-and-wire-hash");
        var recoverable = original with { Recovery = new DesktopRecoveryOptions { Enabled = true } };
        var recoveryHash = Wire.Hash(Wire.Encode(recoverable));
        Check(Wire.Hash(Wire.Encode(ParcelStartupServices.RevalidateDeployment(media, recoverable))) == recoveryHash,
            "Revalidation lost explicitly reviewed recovery policy");
        Check(Invoke("recovery-preflight", Save("recovery", recoverable), 0)["PlanSha256"]!.GetValue<string>() == recoveryHash,
            "Recovery configuration changed the frozen plan");
        cases.Add("explicit-recovery-policy-and-operation-identity-retained-during-revalidation");
        var fresh = ParcelStartupServices.Review(media, runtimeSid, data, seed, null, [], false);
        Check(fresh.OperationId is not null && fresh.OperationId != original.OperationId, "New review reused original operation"); cases.Add("explicit-new-review-has-new-identity");
        foreach (var prefix in "0123456789abcdef")
        {
            var operation = prefix + new string('a', 31);
            Check(WindowsDeploymentSetup.IsInstallationOperationId(operation), "Generated GUID grammar rejected: " + prefix);
            if (prefix is '0' or '9')
            {
                var numericPlan = Save("guid-" + prefix, original with { OperationId = operation });
                _ = Invoke("guid-" + prefix, numericPlan, 0);
            }
        }
        Check(WindowsDeploymentSetup.IsInstallationOperationId("lia-original"), "Current named operation grammar rejected");
        foreach (var invalid in new[] { "", "../escape", "0invalid", new string('0', 32), new string('0', 33), "0123456789ABCDEF0123456789ABCDEF01" })
            Check(!WindowsDeploymentSetup.IsInstallationOperationId(invalid), "Unsafe/noncanonical operation accepted");
        cases.Add("canonical-guid-all-sixteen-leading-digits-and-current-named-identities");

        var receiptFile = Path.Combine(evidence, "preflight-0.json"); var receiptHash = Wire.FileHash(receiptFile);
        Check(DeploymentBatch.Handle(["--deployment-plan", planFile, "--receipt", receiptFile, "--preflight"]), "Duplicate receipt entry not handled");
        var duplicateExit = Environment.ExitCode; Environment.ExitCode = 0;
        Check(duplicateExit == 10 && Wire.FileHash(receiptFile) == receiptHash, "Original receipt overwritten"); cases.Add("duplicate-receipt-refused-with-original-bytes");
        var interruptedReceipt = Path.Combine(evidence, "interrupted-receipt.json"); File.WriteAllText(interruptedReceipt, "{\"Status\":\"Reviewing\"}");
        var interruptedHash = Wire.FileHash(interruptedReceipt);
        _ = DeploymentBatch.Handle(["--deployment-plan", planFile, "--receipt", interruptedReceipt, "--preflight"]);
        var interruptedExit = Environment.ExitCode; Environment.ExitCode = 0;
        Check(interruptedExit == 10 && Wire.FileHash(interruptedReceipt) == interruptedHash, "Interrupted original receipt overwritten"); cases.Add("interrupted-reviewing-receipt-retained");

        var seedFile = Path.Combine(seed, "business.json"); var seedBytes = File.ReadAllBytes(seedFile);
        try { File.AppendAllText(seedFile, "\n "); _ = Invoke("changed-seed", planFile, 2); }
        finally { File.WriteAllBytes(seedFile, seedBytes); }
        cases.Add("changed-seed-bytes-refused-with-valid-json");
        var staleRegistration = Save("stale-registration", original with { ExpectedRegistrationHash = Wire.Hash("stale:" + original.ExpectedRegistrationHash) });
        _ = Invoke("stale-registration", staleRegistration, 2); cases.Add("stale-registration-cas-refused-without-changing-machine-registration");
        var wrongMode = Save("wrong-mode", original with { Mode = original.Mode == RunMode.Simulation ? RunMode.Production : RunMode.Simulation });
        _ = Invoke("wrong-mode", wrongMode, 2); cases.Add("changed-mode-refused-by-complete-request-comparison");
        var invalidOperation = RejectedPlan("invalid-operation", original with { OperationId = "../escape" });
        _ = Invoke("invalid-operation", invalidOperation, 2); cases.Add("invalid-operation-identity-refused");
        var tampered = Path.Combine(evidence, "tampered.plan.json");
        File.WriteAllText(tampered, Wire.Encode(plan with { Deployment = original with { DataRoot = data + "-changed" } }));
        _ = Invoke("tampered", tampered, 2); cases.Add("tampered-plan-hash-refused");

        foreach (var (name, unsupported) in new[] { ("missing-identity", original with { OperationId = null }), ("missing-shortcuts", original with { Shortcuts = null }) })
        {
            var unsupportedFile = RejectedPlan(name, unsupported); var unsupportedHash = Wire.FileHash(unsupportedFile);
            for (var i = 0; i < 2; i++) _ = Invoke(name + "-preflight-" + i, unsupportedFile, 2);
            _ = Invoke(name + "-submit", unsupportedFile, 2, preflight: false);
            Check(Wire.FileHash(unsupportedFile) == unsupportedHash && !Directory.Exists(data), name + ": refusal changed original plan or created deployment data");
            cases.Add(name + "-refused-at-freeze-revalidation-preflight-submit-original-bytes-retained");
        }

        // The real registration remains untouched. Exercise the same receipt
        // guard used by committed re-entry and normal installation completion.
        var committed = new DeploymentLocation(original.ApplicationId, original.SourceCommit, media.Root, seed, original.DataRoot,
            original.RuntimeAccountSid, DeploymentPlanIdentity: plan.PlanSha256, Shortcuts: original.Shortcuts);
        ParcelStartupServices.RequireMatchingInstalledReceipt(original, committed);
        foreach (var unsupported in new[] { original with { OperationId = null }, original with { Shortcuts = null } })
        {
            var refused = false;
            try { ParcelStartupServices.RequireMatchingInstalledReceipt(unsupported, committed with { DeploymentPlanIdentity = Wire.Hash(Wire.Encode(unsupported)), Shortcuts = unsupported.Shortcuts }); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "Unsupported plan was acknowledged as an installed operation");
        }
        cases.Add("installed-receipt-cannot-certify-missing-identity-or-shortcut-selection");
        foreach (var changed in new[]
        {
            committed with { DataRoot = original.DataRoot + "-migrated" },
            committed with { RuntimeAccountSid = "S-1-5-21-synthetic-changed" },
            committed with { SourceCommit = new string('b', 40) },
            committed with { Shortcuts = new(true, false) },
            committed with { Recovery = new DesktopRecoveryOptions { Enabled = true } },
            committed with { DeploymentPlanIdentity = new string('c', 64) },
            committed with { LifecycleState = "RemovedDataRetained" },
            committed with { ApplicationId = "different-application" }
        })
        {
            var refused = false;
            try { ParcelStartupServices.RequireMatchingInstalledReceipt(original, changed); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Changed committed target accepted as the original installation");
        }
        cases.Add("committed-reentry-refuses-registration-target-drift-with-unchanged-plan-fingerprint");

        // Synthetic original journals exercise the actual published batch fence.
        // Never write HKLM, the protected machine store or an installation record.
        var journal = Path.Combine(evidence, "synthetic-original-operation.json");
        DeploymentBatch.RequireUnsubmittedOperation(journal);
        Check(!File.Exists(journal), "Readonly check created an operation");
        foreach (var state in new[] { "Prepared", "ReadyToCommit", "Unknown", "Failed", "Completed" })
        {
            File.WriteAllText(journal, Wire.Encode(new { Status = state, original.OperationId })); var hash = Wire.FileHash(journal);
            for (var retry = 0; retry < 2; retry++)
            {
                var refused = false;
                try { DeploymentBatch.RequireUnsubmittedOperation(journal); } catch (IOException) { refused = true; }
                Check(refused && Wire.FileHash(journal) == hash, state + ": original operation was reused or overwritten");
            }
        }
        File.WriteAllText(journal, "{\"Status\":"); var partialHash = Wire.FileHash(journal); var partialRefused = false;
        try { DeploymentBatch.RequireUnsubmittedOperation(journal); } catch (IOException) { partialRefused = true; }
        Check(partialRefused && Wire.FileHash(journal) == partialHash, "Partial original journal was treated as permission to retry");
        cases.Add("original-prepared-ready-unknown-failed-completed-journals-all-fence-retries");
        Check(Wire.FileHash(planFile) == planHash && Wire.FileHash(receiptFile) == receiptHash, "Original plan/receipt changed after failures");
        Check(Invoke("restored-preflight", planFile, 0)["Status"]!.GetValue<string>() == "PreflightPassed", "Restored unchanged inputs did not recover");
        cases.Add("restored-inputs-repreflight-with-original-identity");
        File.WriteAllText(Path.Combine(evidence, "BATCH-RESULT.json"), Wire.Encode(new { Status = "PASS", MediaSourceCommit = media.SourceCommit,
            BatchBinarySha256 = Wire.FileHash(typeof(DeploymentBatch).Assembly.Location), PlatformBinarySha256 = Wire.FileHash(typeof(WindowsDeploymentSetup).Assembly.Location),
            original.OperationId, plan.PlanSha256, Cases = cases, Installation = "NOT_RUN", MachineRegistrationMutation = "NOT_RUN", Equipment = "NOT_RUN", Scope = "Readonly actual batch entry and synthetic journal fence; not a deployment success" }));
        Console.WriteLine("PASS deployment batch identity, integrity, unsupported input refusal, original receipt and original-operation fences (" + cases.Count + " cases); no installation/device startup");
    }
}
