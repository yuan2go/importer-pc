# REVIEW-FOLLOWUP-11 Implementation Plan

> For agentic workers: execute inline using executing-plans; no separate framework or redesign. The user explicitly authorized implementing the two reviewed corrections and the Product ownership test, publishing new reference packages and opening a main PR.

**Goal:** Restore wave-difference visibility and box-scanner focus, prove composite ownership dynamically and deliver a new same-source reference release.

**Architecture:** Keep one ProductRuntime/Supply owner. Add small window-owned presentation policies, compiled into the existing portable verifier by source link; WPF consumes them. An internal source-factory constructor lets the existing verification friend assembly exercise real Product startup/shutdown with controlled asynchronous source failures. Public startup still uses the registered factory and validates mode/configuration before creating sources.

**Tech Stack:** C#/.NET10.0.402, WPF, SQLite3, existing CLI verifier and Python package/seal scripts.

## Global Constraints

- Preserve SS-UI-VIS-001 v1.1 bytes, schema1/id2/id3 SQL/checksums, original request payload/hash/target/contract and D/RV history.
- Simulation/loopback only. Windows W01-06 NOT_RUN and real B01-06 BLOCKED remain separate from portable PASS.
- No production configuration or CLI fault-injection switch. No private-field reflection or hidden button workaround.

### Task 1: Preserve authority and correct current navigation

- [x] Verify main/parallel PR/workspace; reuse clean isolated checkout and branch from latest main.
- [x] Archive exact touched current docs/manifest, with base Git blobs and SHA.
- [x] Point INDEX/work-package entries to the current scope; preserve previous reports as historical evidence. Correct stale single-channel/800x420 deployment wording without changing the design baseline.

### Task 2: Implement the two UI corrections

Files: `src/Ipc.Desktop/PresentationPolicy.cs`, `ViewModels.cs`, `LaneControl.xaml`, `MainWindow.Presentation.cs`, `tests/Ipc.Verification/PresentationVerification.cs`, verifier csproj/Program.

Interfaces: `WaveComparison.Compare(string? box, string? item)` exposes raw display values and whether both known values differ. `BoxEntryFocusPolicy.Observe(string? session, bool canOpen, Actor? actor)` records state transitions; `TryTake(bool available)` consumes one pending handoff only when safe.

- [x] Verify different/identical/missing/leading-zero wave values and box-focus transition tests.
- [x] Put current Item wave outside the auxiliary Expander; persist the two wave values plus difference in the fixed result area when they differ. Keep the acceptance guard unchanged.
- [x] Observe initial login/new-box/rejected-box transitions; focus and SelectAll once after safe layout, defer while drawer/entry/small/stale, and restore pending handoff when returning. Preserve per-window drafts and avoid timer focus calls without a transition.
- [x] Example assertions: `Assert(!focus.TryTake(true), "Repeated projection cannot steal focus");` and `Assert(WaveComparison.Compare(null, "0001").Differs == false, "Unknown is not mismatch");`.
- [x] Compile WPF and run the real linked presentation policies through `--review-followup`; record actual Windows focus/geometry as NOT_RUN.

### Task 3: Prove Product composite ownership

Files: `src/Ipc.Product/ProductRuntime.cs`, `tests/Ipc.Verification/ProductOwnershipVerification.cs`, Program selector.

- [x] Add an internal constructor taking `Func<DeviceBinding,string,IObservationSource>`; the public constructor delegates to the registered ReferenceProduct factory. Keep validation and lease acquisition ordering.
- [x] Start actual Product with one controlled failing source and one independent source. Prepare a real dropped-response request against the independent receiver before disposal.
- [x] Cases: source Stop throws, Stop task remains pending, Dispose task remains pending. Assert original error/timeout, independent source cleanup, no premature Dispose/racing task, original data/device/station lease contention.
- [x] Reconcile the original request while cleanup is incomplete; assert actual SQLite response and appended journal record, preserved ID/payload/attempt, Stopping barrier and no new admission.
- [x] Complete/recover the held task, retry Dispose, assert exactly-once terminal release and successful restart/lease reacquisition. Keep all injected evidence and final counters.

### Task 4: Freeze source and deliver reference release

- [x] Run locked restore/solution build; `--review-followup`, E07 `--closure`, E08/journey `--followup`. Broaden only on failure/new concern; old migration/T1-T3 evidence remains separately attributed.
- [x] Review diff, commit runtime/config/scripts/packaged operations inputs on the development branch, freeze SourceCommit.
- [x] Build fresh portable and win-x64 reference ZIPs; verify CRC/exact SHA256SUMS/module/config/source identities, run native smoke from fresh extracted portable bytes.
- [x] Save public synthetic logs and checks, precise Windows/integration blockers, report/manifest; preserve existing failure267 archive boundary and old packages.
- [x] Commit documentation, push development branch, create/attach main PR and read back state. Do not merge or force-push.
