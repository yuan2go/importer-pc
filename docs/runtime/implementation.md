# WP-IPC-RUNTIME-03 implementation record

> 当前纠偏执行与结果入口：[AUDIT-RECONCILIATION-01](../work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)。目标设计和下文历史实施/不测试记录保持形成时含义；当前08/10/09实施及验证以该包的同版报告为准。原字节已归档，D/RV不变。

当前独立复核：[MAIN-REVIEW-03](../reviews/main-static-review-03.md)，基准main `2579392`（PR #6/#7已合并）。8项指定整改路径静态补齐，E-06/E-10仍有工程缺口；全部运行NOT_RUN，Windows06/真实集成05分别保持NOT_RUN/BLOCKED。后续见工程增量08、参考验证09；下文07/更早记录保留形成时范围。

当前工程路径与未验证边界见 [ENGINEERING-CLOSURE-07](engineering-closure-07.md)。10项源码整改已实现，Storage3、显式维护和当前UI以07记录为准；本轮未测试/构建/运行，原实施与历史验证不倒写。
This is the retained RUNTIME-03 implementation record. Current alignment gaps, scope and acceptance: [ARCH-UI-ALIGNMENT-01](../work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md). The completion checks below are formation-time RUNTIME-03 evidence, not acceptance of this new package. Retained hardening changes and evidence: [HARDENING-04](hardening-04.md).

Execution starts at main `89089092ad8b7445071e0c12c9c558dc012a26e5`, after the analysis baseline. No existing C# project was found. This is the independent F-02 reference product, not a migrated site application. The approved framework and work package remain the design authority; historical D/RV and restricted assets are unchanged.

## Implementation sequence

- [x] S0: isolate workspace, verify archive baseline, SDK and publication scope.
- [x] S1: `Ipc.Platform` manifest/configuration validation, bounded execution, process ownership and dependency lifecycle; explicit `Ipc.Product` composition.
- [x] S2: `Ipc.Supply` shared application coordinator, command/revision guards, entity/observation identities, reference quantity/disposition/close flows.
- [x] S3: SQLite transactions, immutable requests and attempts, Outbox and recovery; `Ipc.Adapters` two sources and HTTP port; independent `Ipc.Fixture` persistent receiver. Verify with real databases, endpoint and process restarts.
- [x] S4: WPF workstation, history/details, alarms/reconciliation and diagnostics/configuration; stable view models and two layouts/windows sharing one host.
- [x] S5: locked builds, offline publication, backup/preflight/restore, necessary risk regression and deliverable report.

## Current alignment

ALIGNMENT-01 moves hardcoded product/supply validation out of Platform, completes semantic task snapshots and introduces a background single owner. Current paths and limits: [alignment record](alignment-01.md). Prior S4 completion does not imply current Windows/UI acceptance.

## Responsibility and verification

Platform contains no supply rules. Supply owns the command authority, snapshots, store schema and reconciliation decisions. Adapters own observation buffers and protocol translation. Product is the only composition root. Desktop consumes the application facade, never HTTP/database/driver objects. The fixture owns a separate receiving database and atomically arbitrates synthetic demand.

T1 saves state, evidence, immutable intent and Outbox together. T2 claims the original request and records an attempt. T3 saves response/unique acceptance/state together. Network waits occur outside the coordinator and transaction. Dispatching becomes Unknown after restart; Unknown is only queried, never blindly retried. Restored databases enter quarantine. Simulation quantities, roles and physical events are explicitly synthetic.

Risk checks cover AC-01–11 rather than a test-count target. macOS can execute core/fixture/persistence and compile Windows targets, but cannot establish WPF runtime/DPI/touch/SDK/site PASS. See the final delivery record for actual commands and outcomes.

## Inheritance and boundaries

The only existing executable code was `tools/verify_archive.py` and its tests. Both are retained; generated-output scope is narrowed to the seven known project bin/obj roots and `.artifacts`, with a negative test proving documentation and unknown source roots remain checked. There was no inherited business implementation, SDK, Windows deployment or WCS contract.

Platform owns mechanism only; Supply owns persistence schema and business guards; Product is the explicit registration root. Desktop receives `IStationApplication`, never the store, HTTP client or device-source objects. Its source controls call guarded product-level SIMULATION actions. One host/sender/coordinator survives page/layout changes and serves a second window.

SQLite schema1 uses foreign keys, checksummed migration, actual transaction rollback, immutable identity/content/binding checks, unique acceptance and normalized historical records. Restored Prepared intents become Unknown because a snapshot can lag the receiver. Event overflow latches a separate diagnostic and records unadmitted count; admitted events drain without silent discard. Entity permission is a separate epoch/revision/expiry from business revision; after source/process replacement explicit synthetic revalidation is required.

Configuration promotion is deferred until owned modules actually initialize. A missing replay source records ActivationFailed and retains the previous effective configuration for the next controlled start. No new endpoint is applied to old requests. Simulation identity is reset on every process start and frozen in each original payload.

Reference cleanup requires maintenance evidence and an explicit synthetic retention period; it preserves unresolved/current aggregates and referenced configurations. Diagnostic export is metadata-only by default. Windows scripts are syntax-checked separately from desktop/Windows operational validation.
