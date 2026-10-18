# Sportmaster RCS Task Implementation Plan

> For agentic workers: execute this approved plan in this session, with focused verification before publication. User has already authorized implementation and main PR.

**Goal:** Deliver the first RCS destination-request component through the real Sportmaster application path.

**Architecture:** Signed IWcsExtension + optional pure IWcsRequestPreparer. Application owns UnpackRequired and durable T1/T2/T3; adapter owns HTTP and protocol classification.

**Tech Stack:** Existing .NET 10.0.402, System.Net.Http, System.Text.Json, existing SQLite and signed extension tooling. No new NuGet dependency.

## Global Constraints

Keep SQL1-4, original request/reply bytes, D/RV and parallel PR26. No retry, redirects, invented query/physical completion or production fallback. Only synthetic fixture data is published. Windows actual NOT_RUN; real RCS/device/field BLOCKED.

### Task 1: Durable preparation and result contracts

Files: src/Ipc.Application.Contracts/ParcelContracts.cs, WcsDestination.cs; src/Ipc.Application.ParcelFlow/ParcelRuntime.cs, ParcelHistory.cs, ParcelRepository.cs; src/Ipc.Application.Sportmaster/Application.cs.

- [x] Add optional PreparedIntegrationRequest and typed destination/HTTP receipt fields, IWcsRequestPreparer, appended UnpackRequired state, contract version increment.
- [x] Pure preparer receives detached FrozenRequest and persistent operation sequence; T1 commits returned body/hash before any call. SQL5 adds future-reader semantic guard; retain upgrade settlement check.
- [x] Sportmaster maps NEED_UNPACK to UnpackRequired and holds this position until inspected local disposition; generic rejection remains distinct.

### Task 2: Protocol component and executable configuration

Files: src/Ipc.Integrations.SportmasterRcs/*, ImporterPc.slnx, config/sportmaster/rcs.parameters.json, scripts/release_documents.py.

- [x] Serialize typed camelCase envelope with explicit null chute and numeric requestId; validate binding/position/range/quantity/target.
- [x] Send only frozen body with reusable bounded HttpClient, no redirects/cookies/proxy/retry. Preserve raw response before classifying normal/unpack/rejected/unknown.
- [x] Provide committed extension.build.json, manifest, modules and parameter schema for existing signing/installation chain; expose RequestDestinationAsync and IWcsPort.

### Task 3: Verification and delivery

Files: tests/Ipc.Application.Verification/SportmasterRcs.cs, Program.cs, csproj; docs/runtime/sportmaster-rcs-task.md; docs/INDEX.md; docs/manifest.json.

- [x] Run affected existing baseline using `dotnet run --project tests/Ipc.Application.Verification -- . --application-architecture`; retain evidence.
- [x] Add `--sportmaster-rcs` covering real loopback HTTP through application, three business replies, malformed/correlated/HTTP failure, timeout, parallel positions, restart no retry, frozen request/response and DB compatibility.
- [x] Build necessary host/WPF, commit source, use frozen host to build/check/sign extension, verify signed application assembly, portable package and extraction startup.
- [x] Record exact source/package hashes and PASS/NOT_RUN/BLOCKED; update navigation/manifest and commit source.
- [ ] Push final branch and create main PR; do not merge.
