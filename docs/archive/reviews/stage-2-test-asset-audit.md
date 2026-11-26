# Stage 2 测试资产审计表

盘点基线为 Stage 1 提交 3ac73fbd。文件和行数取自 Git 跟踪的 C# 源文件，项目归属按目录统计。

| 工程 | C# 文件 | 源码行 |
|---|---:|---:|
| Ipc.Application.Verification | 202 | 30915 |
| Ipc.Core.Tests | 6 | 519 |
| Ipc.Deployment.Verification | 2 | 201 |
| Ipc.Desktop.UiVerification | 6 | 573 |
| Ipc.Drivers.Hikrobot.Probe | 17 | 1693 |
| Ipc.Hikrobot.IndependentReview | 1 | 276 |
| Ipc.Hikrobot.LocalSimulation | 1 | 220 |
| Ipc.Installer.Verification | 1 | 91 |
| Ipc.Operations.Verification | 3 | 924 |
| Ipc.Verification | 29 | 5140 |

## 核心入口及调用关系

75 个 DataRow 逐项启动验证程序。下表包含 Application 调度入口、首参数子进程及离线探针。消费方同时查找带前缀参数和工作流拼接的名称。同名桌面入口需按实际可执行程序复核。未发现调用仅是删除候选。

| 原入口 | 调度目标 | 引用线索 |
|---|---|---|
| <code>--sm-presence-replay</code> | PresenceSampleReplay.Run | sm-parcel-rounds.md |
| <code>--ordinary-start-child</code> | OrdinaryRuntimeStartup.Child | OrdinaryRuntimeStartup.cs |
| <code>--installation-storage-helper</code> | InstallerStorageMatrix.Helper | InstallerStorageMatrix.cs |
| <code>--installation-storage-ordinary-child</code> | InstallerStorageMatrix.OrdinaryChild | InstallerStorageMatrix.cs |
| <code>--maintenance-authority-probe</code> | MaintenanceAuthorizationStartup.Probe | MaintenanceAuthorizationStartup.cs |
| <code>--ordinary-maintenance-child</code> | MaintenanceAuthorizationStartup.OrdinaryChild | MaintenanceAuthorizationStartup.cs |
| <code>--action</code> | Console.WriteLine, Wire.Encode, ParcelRecovery.InspectAnalysisBackup | application.ps1, application_bundles.py, developer-guide.md, extension_build.py, operations.md, package-application.py, reconcile-retention-qualification.py, smoke-extension.py, smoke.py, stability-qualification.py, station.ps1, supply-getting-started.md, supply-guide.md, test_retention_reconciliation.py, test_runtime_identity.py, verify-retention-maintenance.ps1 |
| <code>--cr-report-boundaries</code> | CrReportBoundaryChecks.Run | CoreRegression.cs |
| <code>--cr-report-cameras</code> | CrReportCameraChecks.Run | CoreRegression.cs, CrReportBoundaryChecks.cs |
| <code>--cr-sm-removal</code> | CrVerifiedSmChecks.Removal | CoreRegression.cs |
| <code>--cr-sm-dispatch</code> | CrVerifiedSmChecks.Dispatch | CoreRegression.cs |
| <code>--cr-sm-live-devices</code> | CrVerifiedSmChecks.LiveDevices | CoreRegression.cs |
| <code>--cr-sm-backup-ownership</code> | CrVerifiedSmChecks.BackupOwnership | CoreRegression.cs |
| <code>--cr-sm-backup-fences</code> | CrVerifiedSmChecks.BackupFences | CoreRegression.cs |
| <code>--cr-sm-templates</code> | CrVerifiedSmChecks.Templates | CoreRegression.cs |
| <code>--cr-sm-preferences</code> | CrVerifiedSmChecks.Preferences | CoreRegression.cs |
| <code>--mvs-presence</code> | MvsPresenceChecks.Run | CoreRegression.cs, current-contract-cleanup-01.md |
| <code>--sm-single-lane-calibration</code> | SmSingleLaneCalibration.Run | CoreRegression.cs |
| <code>--sm-single-station</code> | SmSingleStationChecks.Run | CoreRegression.cs |
| <code>--sm-arrival-cameras</code> | Ipc.Verification.SmArrivalCameraChecks.Run | CoreRegression.cs, sm-continuous-induction-closure-01.md |
| <code>--sm-arrivals</code> | Ipc.Verification.SmArrivalChecks.Run | CoreRegression.cs, current-contract-cleanup-01.md, sm-continuous-induction-closure-01.md |
| <code>--sm-round-lifecycle</code> | Ipc.Verification.SmRoundLifecycle.Run | CoreRegression.cs, current-contract-cleanup-01.md, sm-continuous-induction-closure-01.md |
| <code>--sm-dispatch-resources</code> | SmDispatchResources.Run | CoreRegression.cs, current-contract-cleanup-01.md, sm-continuous-induction-closure-01.md, sm-parcel-rounds.md, win11-24x7-stability-remediation-01.md |
| <code>--startup-camera-dependencies</code> | StartupCameraDependencies.Run | CoreRegression.cs, mvid-native-reader.md |
| <code>--backup-set-delivery</code> | BackupSetDeliveryChecks.Run | CoreRegression.cs |
| <code>--installation-resource-boundary</code> | SportmasterRcs.InstallationBoundaryChecks | CoreRegression.cs |
| <code>--backup-consistency</code> | BackupConsistency.Run | CoreRegression.cs, current-contract-cleanup-01.md |
| <code>--stability-analysis</code> | StabilityAnalysis.Run | CoreRegression.cs, current-contract-cleanup-01.md |
| <code>--debug-no-isolation-31</code> | DebugNoIsolation31.Run | CoreRegression.cs, current-contract-cleanup-01.md |
| <code>--installer-storage-checks</code> | InstallerStorageChecks.Run | CoreRegression.cs |
| <code>--installer-storage-matrix</code> | Array.IndexOf, InstallerStorageMatrix.Start | 无外部引用线索 |
| <code>--installer-storage-matrix-direct</code> | Array.IndexOf, InstallerStorageMatrix.Run | verify-installation-storage.ps1 |
| <code>--runtime-identity-boundary</code> | RuntimeIdentityBoundary.Run | test_runtime_identity.py 核验夹具源文件，现纳入核心测试 |
| <code>--ordinary-runtime-startup</code> | OrdinaryRuntimeStartup.Run | hikrobot-sdk.yml, sportmaster-commercial.yml |
| <code>--maintenance-authorization-startup</code> | MaintenanceAuthorizationStartup.Run | 无外部引用线索 |
| <code>--maintenance-launch-fence</code> | OperatingSystem.IsWindows, MaintenanceLaunchFence.Run | 无外部引用线索 |
| <code>--session-shift-crash-child</code> | Array.IndexOf, SessionShift25.CrashChild | SessionShift25.cs |
| <code>--session-shift-25</code> | SessionShift25.Run | verify-sm-session-shift-25.ps1 |
| <code>--ui-spec-24</code> | UiSpec24.Run | verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--product-closure-21</code> | StationProductClosure.Run | WindowEventShim.cs, verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--management-status-ownership</code> | StabilityRemediation.OwnedManagementStatus | CoreRegression.cs |
| <code>--state-journal</code> | StateJournalChecks.Run | CoreRegression.cs |
| <code>--read-cadence</code> | ReadCadenceChecks.Run | CoreRegression.cs, sm-dual-camera-performance.md |
| <code>--interrupted-scan-skip</code> | InterruptedScanSkipChecks.Run | CoreRegression.cs, sm-dual-camera-performance.md |
| <code>--management-workspace</code> | ManagementWorkspaceChecks.Run | CoreRegression.cs, INDEX.md |
| <code>--station-position-control</code> | StationPositionControlChecks.Run | CoreRegression.cs, INDEX.md, sm-continuous-induction-closure-01.md |
| <code>--manual-supply</code> | ManualSupplyChecks.Run | verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--manual-rcs-debug-lifecycle</code> | ManualRcsDebugChecks.Lifecycle | 无外部引用线索 |
| <code>--manual-rcs-debug</code> | ManualRcsDebugChecks.Run | CoreRegression.cs |
| <code>--camera-rounds</code> | CameraRoundChecks.Run | CoreRegression.cs, current-contract-cleanup-01.md, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--camera-single-frame-recovery</code> | CameraSingleFrameRecovery.Run | 无外部引用线索 |
| <code>--camera-chain-reliability</code> | CameraChainReliability.Run | CoreRegression.cs, sm-continuous-induction-closure-01.md, verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1 |
| <code>--camera-chain-closure</code> | CameraChainClosure.Run | verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--reader-feedback-22</code> | ReaderFeedback22.Run | CoreRegression.cs, verify-reader-feedback-22.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--unread-package-27</code> | UnreadPackage27.Run | 无外部引用线索 |
| <code>--reader-robustness-28</code> | ReaderRobustness28.Run | CoreRegression.cs |
| <code>--sm-automatic-rcs</code> | SportmasterRcs.AutomaticRcs | CoreRegression.cs, current-contract-cleanup-01.md, sportmaster-rcs-task.md |
| <code>--installation-configuration</code> | InstallationConfigurationChecks.Run | CoreRegression.cs |
| <code>--industrial-lifecycle</code> | IndustrialLifecycle.Run | CoreRegression.cs |
| <code>--industrial-rollback</code> | IndustrialRollback.Run | CoreRegression.cs |
| <code>--industrial-version-pruning</code> | IndustrialVersionPruning.Run | CoreRegression.cs |
| <code>--permanent-deletion-02</code> | PermanentDeletion02.Run | CoreRegression.cs |
| <code>--uninstall-data-boundary</code> | UninstallDataBoundary.Run | CoreRegression.cs |
| <code>--uninstall-assets</code> | UninstallAssetsChecks.Run | CoreRegression.cs |
| <code>--industrial-opaque-removal</code> | IndustrialOpaqueRemoval.Run | CoreRegression.cs |
| <code>--industrial-quiet-deployment</code> | IndustrialQuietDeployment.Run | CoreRegression.cs |
| <code>--industrial-diagnostics</code> | IndustrialDiagnostics.Run | CoreRegression.cs |
| <code>--industrial-recovery</code> | IndustrialRecovery.Run | CoreRegression.cs, IndustrialRecovery.cs |
| <code>--lifecycle-restore</code> | LifecycleRestore.Run | CoreRegression.cs |
| <code>--operator-corrections-29</code> | OperatorCorrections29.Run | 无外部引用线索 |
| <code>--same-placement-29</code> | SamePlacement29.Run | CoreRegression.cs, MvsSoftwareFlow.cs, SamePlacement29.cs |
| <code>--live-device-cleanup</code> | LiveDeviceCleanup.Run | 无外部引用线索 |
| <code>--sm-runtime-responsibility</code> | SmRuntimeResponsibility.Run | CoreRegression.cs, SmRuntimeResponsibility.cs |
| <code>--live-devices-30</code> | LiveDeviceCleanup.Run, LiveDevices30.Run | CoreRegression.cs, LiveDevices30.cs |
| <code>--mvs-stop-reconnect</code> | MvsStopReconnect.Run | CoreRegression.cs |
| <code>--mvs-still-frames</code> | MvsStillFrames.Run | CoreRegression.cs |
| <code>--log-readable</code> | LogReadable.Run | CoreRegression.cs, CrReportBoundaryChecks.cs |
| <code>--work-card-current</code> | WorkCardCurrent.Run | CoreRegression.cs, WorkCardCurrent.cs |
| <code>--history-detail-sheet</code> | HistoryDetailSheet.Run | CoreRegression.cs |
| <code>--installation-receipts-04</code> | InstallationReceipts04.Run | CoreRegression.cs, InstallationReceipts04.cs |
| <code>--first-configuration-03</code> | FirstConfiguration03.Run | CoreRegression.cs |
| <code>--dual-camera-performance</code> | DualCameraPerformance.Run | CoreRegression.cs, package-application.py, sm-dual-camera-performance.md, stability-qualification.py, test_dual_camera_performance.py |
| <code>--reader-ingress-terminal-22</code> | ReaderIngressTerminal22.Run | verify-reader-feedback-22.ps1 |
| <code>--sm-original-frame</code> | SportmasterRcs.OriginalFrameChecks | CoreRegression.cs, verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1 |
| <code>--shutdown-resources</code> | SportmasterRcs.ShutdownResources | verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--mvs-contrast-checks</code> | SportmasterRcs.MvsContrastChecks | 无外部引用线索 |
| <code>--mvs-latency</code> | SportmasterRcs.MvsLatency | measure-mvs-latency.py |
| <code>--decode-format-benchmark</code> | SportmasterRcs.DecodeFormatBenchmark | 无外部引用线索 |
| <code>--decode-tilt-benchmark</code> | DecodeTiltBenchmark.Run | CrReportBoundaryChecks.cs, DecodeTiltBenchmark.cs |
| <code>--mvid-runtime-probe</code> | MvidReader.ProbeRuntime | mvid-native-reader.md |
| <code>--mvid-reader</code> | MvidReader.Run | CoreRegression.cs, MvidReader.cs, current-contract-cleanup-01.md, mvid-native-reader.md, sportmaster-commercial.yml, win11-24x7-stability-remediation-01.md |
| <code>--tilted-decode</code> | TiltedDecode.Run | CoreRegression.cs |
| <code>--mvs-software-flow</code> | SportmasterRcs.MvsSoftwareFlow | hikrobot-sdk.yml, sportmaster-rcs-task.md, verify-sm-product-closure-21.ps1, verify-sm-sdk-python.py |
| <code>--mvs-offline-activation</code> | SportmasterRcs.MvsOfflineActivation | CoreRegression.cs, verify-sm-product-closure-21.ps1 |
| <code>--mvs-network-reconnect</code> | MvsNetworkReconnect.Run | CoreRegression.cs, mvs-software-reader-01.md |
| <code>--sm-built-in-only</code> | SportmasterRcs.BuiltInOnly | CoreRegression.cs, sportmaster-commercial.yml |
| <code>--station-single-camera</code> | SportmasterRcs.SingleCamera | CoreRegression.cs, sportmaster-commercial.yml |
| <code>--hikrobot-sdk-editing</code> | HikrobotSdkEditing.Run | mvid-native-reader.md, verify-reader-feedback-22.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--hikrobot-hardening</code> | HikrobotHardening.Run | hikrobot-sdk.yml |
| <code>--storage-baseline</code> | StorageBaseline.Run | current-contract-cleanup-01.md, sportmaster-commercial.yml |
| <code>--storage-schema-only</code> | StorageBaseline.Run（只查格式，与上一行共用分支） | CoreRegression.cs、验证脚本 |
| <code>--supervisor-continuity-child</code> | SupervisorContinuity.Child | SupervisorContinuity.cs |
| <code>--time-storage-exit-child</code> | Task.Delay, Environment.Exit, Array.IndexOf | TimeStorageProcessChecks.cs |
| <code>--time-storage-process-checks</code> | Console.WriteLine, TimeStorageProcessChecks.Processes | sportmaster-commercial.yml |
| <code>--time-storage-runtime-child</code> | Array.IndexOf, TimeStorageProcessChecks.Child | TimeStorageProcessChecks.cs |
| <code>--time-storage-supervisor-checks</code> | Console.WriteLine, TimeStorageProcessChecks.Supervisor, SupervisorContinuity.Run, Path.Combine | sportmaster-commercial.yml |
| <code>--history-open-fixture</code> | HistoryOpenBenchmark.CreateFixture, Array.IndexOf | compare-history-open.py |
| <code>--history-open-worker</code> | HistoryOpenBenchmark.Worker, Array.IndexOf | compare-history-open.py |
| <code>--time-storage-atomic-checks</code> | Console.WriteLine, AtomicStorageChecks.Run | sportmaster-commercial.yml |
| <code>--time-storage-fault-checks</code> | Console.WriteLine, TimeStorageFaultChecks.Run, StartupStorageBoundaries.Run, Path.Combine | sportmaster-commercial.yml |
| <code>--stability-audit-fixes</code> | StabilityAuditFixes.Run | sportmaster-commercial.yml |
| <code>--stability-audit-history</code> | StabilityAuditFixes.History | 无外部引用线索 |
| <code>--stability-audit-maintenance</code> | StabilityAuditFixes.Maintenance | 无外部引用线索 |
| <code>--lifecycle-ux</code> | LifecycleOptimization.Run, MaintenanceProgressChecks.Run, Path.Combine | sportmaster-commercial.yml |
| <code>--maintenance-progress-checks</code> | MaintenanceProgressChecks.Run | 无外部引用线索 |
| <code>--latency-capacity-checks</code> | LatencyCapacityChecks.Run | sportmaster-commercial.yml |
| <code>--latency-projection-checks</code> | LatencyProjectionChecks.Run | sportmaster-commercial.yml |
| <code>--latency-details-checks</code> | LatencyDetailsChecks.Run | sportmaster-commercial.yml |
| <code>--latency-storage-checks</code> | LatencyStorageChecks.Run | sportmaster-commercial.yml |
| <code>--p0-provider-identity</code> | SportmasterRcs.ProviderIdentity | sportmaster-commercial.yml |
| <code>--p0-deferred</code> | SportmasterRcs.DeferredMatrix | sportmaster-commercial.yml |
| <code>--p0-rcs</code> | SportmasterRcs.P0ScanRegression | sportmaster-commercial.yml |
| <code>--p0-cleanup</code> | P0Cleanup.Run | p0-stability-01.md, sportmaster-commercial.yml |
| <code>--p0-tcp</code> | P0Tcp.Run | sportmaster-commercial.yml |
| <code>--p0-tcp-components</code> | P0Tcp.ExistingComponents | sportmaster-commercial.yml |
| <code>--p0-crash-child</code> | Array.IndexOf, P0Recovery.Child | P0Stability.Recovery.cs |
| <code>--p0-recovery</code> | P0Recovery.Run | sportmaster-commercial.yml |
| <code>--p0-supervisor</code> | P0Supervisor.Run | sportmaster-commercial.yml |
| <code>--p0-task-validate</code> | P0Supervisor.ValidateTaskSchema | sportmaster-commercial.yml |
| <code>--p0-task-lookup</code> | P0TaskLookup.Run | sportmaster-commercial.yml |
| <code>--p0-supervised-child</code> | Task.Delay, Environment.Exit | P0Stability.Supervisor.cs |
| <code>--p0-unknown-restart</code> | SportmasterRcs.P0UnknownRestart | sportmaster-commercial.yml |
| <code>--parameter-guidance-startup</code> | ParameterGuidanceStartup.Run | CoreRegression.cs, WindowEventShim.cs, current-contract-cleanup-01.md, sportmaster-commercial.yml, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--deferred-connections</code> | SportmasterRcs.DeferredConnectionChecks | sportmaster-commercial.yml |
| <code>--h02-crash-child</code> | Array.IndexOf, ReliabilityHardening.CrashChild | ReliabilityHardening.Crashes.cs |
| <code>--h02-crashes</code> | ReliabilityHardening.Crashes | code-reliability-hardening-02.md, sportmaster-commercial.yml |
| <code>--reliability-02</code> | ReliabilityHardening.Run | code-reliability-hardening-02.md, sportmaster-commercial.yml, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--retention-ingress-plan</code> | Array.IndexOf, RetentionIngress.ExplainClosedBackup | 无外部引用线索 |
| <code>--retention-ingress-crash-child</code> | Array.IndexOf, RetentionIngress.CrashChild | RetentionIngress.cs |
| <code>--retention-ingress</code> | RetentionIngress.Run | sportmaster-commercial.yml, verify-sm-product-closure-21.ps1 |
| <code>--retention-qualification</code> | Array.IndexOf, RetentionQualification.Run | RetentionQualification.cs, check-retention-reconciliation.py, reconcile-retention-qualification.py, verify-retention-maintenance.ps1 |
| <code>--win11-stability</code> | StabilityRemediation.Run | CoreRegression.cs, sportmaster-commercial.yml |
| <code>--stability-history-scale</code> | StabilityHistoryScale.Run | 无外部引用线索 |
| <code>--dual-camera-communication</code> | DualCameraCommunication.Run | sportmaster-commercial.yml |
| <code>--parameter-inheritance</code> | ParameterInheritance.Run | current-contract-cleanup-01.md, parameter-inheritance-02.md, sportmaster-commercial.yml |
| <code>--aqa01-crash-child</code> | Array.IndexOf, ArchitectureQualityRemediation.CrashChild | ArchitectureQualityRemediation.Boundaries.cs, ArchitectureQualityRemediation.cs |
| <code>--architecture-quality-remediation</code> | ArchitectureQualityRemediation.Run | sportmaster-commercial.yml |
| <code>--administrator-broker-boundary</code> | OperatingSystem.IsWindows, AdministratorBoundary.Run, Array.IndexOf | 无外部引用线索 |
| <code>--management-mode</code> | OperatorAdministration.Run | AdministratorBoundary.cs, CommercialProductization.cs, CoreRegression.cs, IndustrialRecovery.cs, OperatorAdministration.cs, PermanentDeletion02.cs, sportmaster-commercial.yml, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--sm-parameters</code> | SmParameters.Run | CoreRegression.cs, current-contract-cleanup-01.md |
| <code>--parameter-refactor</code> | ParameterRefactor.Run, ParameterInheritance.Run | CoreRegression.cs, current-contract-cleanup-01.md, parameter-inheritance-02.md, sportmaster-commercial.yml |
| <code>--installer-options</code> | InstallerOptions.RequireCodePage, Array.IndexOf, InstallerOptions.Run | current-contract-cleanup-01.md, sportmaster-commercial.yml, verify-shortcut-codepages.ps1 |
| <code>--shortcut-lifecycle</code> | OperatingSystem.IsWindows, InstallerOptions.RequireCodePage, Array.IndexOf, ShortcutLifecycle.Run | CoreRegression.cs, sportmaster-commercial.yml, verify-shortcut-codepages.ps1 |
| <code>--installer-change-guards</code> | InstallerChangeGuards.Run | sportmaster-commercial.yml |
| <code>--windows-client-compatibility</code> | WindowsClientCompatibilityChecks.Run | sportmaster-commercial.yml |
| <code>--commercial-components</code> | Array.IndexOf, CommercialComponentReview.Run, Path.GetFullPath | verify-commercial-components.ps1 读取固定提交的夹具源文件 |
| <code>--commercial-productization</code> | CommercialProductization.Run | sportmaster-commercial.yml |
| <code>--startup-deployment</code> | StartupDeployment.Run | 无外部引用线索 |
| <code>--ux-commercial-presentation</code> | UxCommercialPresentation.Run | sportmaster-commercial.yml |
| <code>--ux-commercial-configuration</code> | ArchitectureQuality.UxCommercialConfigurationChecks | verify-sm-product-closure-21.ps1 |
| <code>--ux-commercial-inputs</code> | UxCommercialInputs.Run | 无外部引用线索 |
| <code>--resource-risks</code> | ResourceRisks.Run | 无外部引用线索 |
| <code>--audit03-maintenance</code> | Audit03.Maintenance | 无外部引用线索 |
| <code>--audit03-snapshots</code> | Audit03.Snapshots | sportmaster-commercial.yml |
| <code>--commercial-quality</code> | CommercialQuality.Run | CommercialQuality.cs, sportmaster-commercial.yml |
| <code>--audit03-rcs</code> | SportmasterRcs.Audit03Checks | sportmaster-commercial.yml, sportmaster-rcs-task.md |
| <code>--audit03-input</code> | Audit03.Inputs | 无外部引用线索 |
| <code>--sportmaster-window-lifecycle</code> | SportmasterWindowLifetime.Run | 无外部引用线索 |
| <code>--sportmaster-ui</code> | SportmasterUi.Run | 无外部引用线索 |
| <code>--storage-format-guards</code> | ArchitectureQuality.StorageFormatChecks | sportmaster-commercial.yml |
| <code>--rcs-heartbeat</code> | SportmasterRcs.HeartbeatChecks | CoreRegression.cs, sportmaster-rcs-task.md |
| <code>--rcs-timeout-errors</code> | SportmasterRcs.TimeoutErrorChecks | CoreRegression.cs, current-contract-cleanup-01.md, sportmaster-commercial.yml, sportmaster-rcs-task.md, verify-reader-feedback-22.ps1, verify-sm-product-closure-21.ps1, verify-sm-session-shift-25.ps1, verify-sm-ui-spec-24.ps1 |
| <code>--sportmaster-rcs</code> | SportmasterRcs.Run | BuiltInOnly.cs, CameraRoundHarness.cs, ManualSupplyChecks.cs, MvsOfflineActivation.cs, MvsSoftwareFlow.cs, OperatorAdministration.cs, P0SimulationFactory.cs, P0Stability.Deferred.cs, P0Stability.Scans.cs, P0Stability.UnknownRestart.cs, RcsConnectionEvidence.cs, RcsExactDeadline.cs, RcsTimeoutErrors.cs, ShutdownResources.cs, SmAutomaticRcs.cs, SmOriginalFrames.cs, StationSingleCamera.cs, VersionCompatibility.cs, sportmaster-commercial.yml, sportmaster-rcs-task.md, verify-sm-product-closure-21.ps1 |
| <code>--version-compatibility</code> | VersionCompatibility.Run | sportmaster-commercial.yml |
| <code>--signed-business-compatibility</code> | Array.IndexOf, SignedCapabilities.Run, Path.GetFullPath | 无外部引用线索 |
| <code>--configuration-confirmation</code> | ArchitectureQuality.ConfigurationConfirmationChecks | sportmaster-commercial.yml |
| <code>--configuration-confirmation-crash-child</code> | Array.IndexOf, ArchitectureQuality.ActivationCrashChild | ArchitectureQuality.ConfigurationConfirmation.cs |
| <code>--raw-custody-child</code> | Array.IndexOf, ArchitectureQuality.RawCustodyChild | ArchitectureQuality.Custody05.cs, ArchitectureQuality.RecoveryP2.cs |
| <code>--parcel-recovery-p2</code> | ArchitectureQuality.RecoveryP2Checks | sportmaster-commercial.yml |
| <code>--history-session-contract</code> | HistorySessionContractChecks.Run | sportmaster-commercial.yml |
| <code>--current-offline-recovery-child</code> | Array.IndexOf, ArchitectureQuality.CurrentOfflineRecoveryChild | ArchitectureQuality.OfflineRecoveryCuts.cs |
| <code>--receipt-custody</code> | ArchitectureQuality.CustodyChecks | sportmaster-commercial.yml |
| <code>--upload-crash-child</code> | Array.IndexOf, ArchitectureQuality.UploadCrashChild | ArchitectureQuality.Recovery02.cs |
| <code>--configuration-crash-child</code> | Array.IndexOf, ArchitectureQuality.ActivationCrashChild | ArchitectureQuality.Activation.cs |
| <code>--quality-extensions</code> | Array.IndexOf, ArchitectureQuality.ExtensionChecks, Path.GetFullPath | 无外部引用线索 |
| <code>--inventory-closure</code> | ArchitectureQuality.InventoryClosureChecks | 无外部引用线索 |
| <code>--review-closure</code> | ArchitectureQuality.ReviewClosureChecks | sportmaster-commercial.yml |
| <code>--architecture-quality</code> | ArchitectureQuality.Run | sportmaster-commercial.yml |
| <code>--acquisition-lifecycle</code> | AcquisitionLifecycle.Run | CoreRegression.cs |
| <code>--application-architecture</code> | ApplicationArchitecture.Run | application-architecture-22.md, sportmaster-commercial.yml |
| <code>--qualified-capabilities</code> | Array.IndexOf, SignedCapabilities.Run, Path.GetFullPath | 无外部引用线索 |
| <code>--signed-capabilities</code> | Array.IndexOf, SignedCapabilities.Run, Path.GetFullPath | 无外部引用线索 |
| <code>--review-regressions</code> | ReviewRegressions.Run | 无外部引用线索 |
| <code>--reliability-closure</code> | ReliabilityClosure.Run | 无外部引用线索 |
| <code>--backup-health-recovery</code> | BackupHealthRecovery.Run | sportmaster-commercial.yml |
| <code>--capability-closure</code> | CapabilityClosure.Run | sportmaster-commercial.yml |
| <code>--site-capabilities</code> | OptionalCapabilities.Run | 无外部引用线索 |

## 编号套件复核

| 原入口 | 应保留的独特边界 |
|---|---|
| <code>--cr-sm-removal</code> | 移出后原请求围栏与永久防重 |
| <code>--cr-report-boundaries</code> | 配置归属、导出身份与到期、部署回执、归档提交及进程隔离 |
| <code>--cr-report-cameras</code> | SDK 帧借用归还、心跳中断及到位确认 |
| <code>--cr-sm-dispatch</code> | 调度占用与左右位独立 |
| <code>--cr-sm-live-devices</code> | 热替换失败与旧设备释放 |
| <code>--cr-sm-backup-ownership</code> | 备份所有者及已发布集合 |
| <code>--cr-sm-backup-fences</code> | 备份冻结与原请求责任 |
| <code>--cr-sm-templates</code> | 首配模板的字段及确认 |
| <code>--cr-sm-preferences</code> | 按用户保存的显示和声音 |
| <code>--reader-feedback-22</code> | 识读反馈及持久输入 |
| <code>--same-placement-29</code> | 同次放置的重复输入 |
| <code>--reader-robustness-28</code> | 多候选与读码器恢复 |
| <code>--live-devices-30</code> | 在线设备分步替换 |
| <code>--debug-no-isolation-31</code> | 维护调试独立发送 |
| <code>--first-configuration-03</code> | 首次配置草稿 |
| <code>--installation-receipts-04</code> | 安装回执与重开 |
| <code>--permanent-deletion-02</code> | 永久删除白名单及共享引用 |

## 假设与脆弱点

- 备份测试在调用方上启动可能同步运行的复制回调，再等待同一回调进入。应分别观察工作完成和进入阶段，并在退出时解开屏障。
- 在位模型使用显式单调时间戳检验 300 ms 移出与 800 ms 校准。整链相机模拟采用真实帧间隔，跨线程等待需观察已处理帧或当前工位确认。
- 到位测试的自动心跳和手动心跳共享单在途许可。观察真实响应和车号，单次检查返回 Busy 时不能当作有效回包。
- sportmaster.synthetic.json 和内置 RCS 的工位、模板、用户设置均有明确业务归属。数据库格式、备份文件保管与安装资料需区分共享产品行为及实际使用的 SM 测试宿主。
- 流程使用的 p0、h02、audit03、现场回放以及由父进程传入的子进程入口须沿调用图保留。

## 重复断言与相似名称

- 原生缓存排空的三条断言使用相同的返回码序列。软件路线与原生路线曾各跑一次；现在由原生路线执行一次，两条路线仍分别测试离线预览和恢复识读。
- 原备份用例两次等待同一个同步事件，且在另一条已明确合格的空盘事实之前用壁钟证明三帧耗时。复制阶段现在只等一次。三帧完成按帧号观测，像素阈值仍由 MvsPresenceChecks 使用可控采集时间核验。
- 安装回执的 1030 条记录排序、跨业务备份删除围栏、旧轮次人工令牌拒绝均有独立失败条件，移入领域套件后继续执行。商业卸载内核和连续供包移出都曾称为 removal，两者分别保留。
- 相机替换时释放失败、冻结请求的 CAS 保护、配置模板与按用户保存的偏好属于不同边界。合并启动入口只减少验证进程启动次数，原断言仍落在对应场景。

## 无直接调用的入口如何处理

在 197 个调度入口中，55 个未在原核心行、工作流或脚本里直接出现。一个入口可以同时由脚本和核心回归调用。离线探针、动态拼接和子进程均单独追踪。

| 复核发现 | 处置依据 |
|---|---|
| p0、h02、audit03、ui-spec-24、product-closure-21 | 商用工作流的 shell 循环或 PowerShell 的应用套件清单实际执行这些测试。保留仍在用的子进程参数。桌面程序另有同名参数。 |
| runtime-identity-boundary、maintenance-launch-fence | 运行时身份拒绝与命名管道启动取消有独立断言，已纳入 Platform 核心回归。 |
| maintenance-authorization-startup、installer-storage-matrix、administrator-broker-boundary、review-regressions | 专用验证涉及提权、普通账户或受保护机器策略。保留父子进程及证据校验。本机直接运行 review-regressions 时，资格目录所有者检查拒绝，包签名部分实际通过。 |
| sm-presence-replay、mvid-runtime-probe、decode-*-benchmark、stability-history-scale、retention-ingress-plan | 分别消费采样清单、原生运行库、测量参数、规模夹具或离线文件。用途与安装参数见各入口和上表。 |
| manual-rcs-debug-lifecycle、camera-single-frame-recovery、live-device-cleanup、stability-audit-history、stability-audit-maintenance、maintenance-progress-checks、resource-risks、mvs-contrast-checks | 删除重复入口。同一方法仍由调试、相机链、热换、审计总套件、生命周期、默认套件及海康 SDK 工作流执行。 |
| commercial-components | 固定来源脚本读取指定提交的 CommercialComponentReview.cs。保留源码，删除当前程序内无调用的同名包装。 |
| unread-package-27、operator-corrections-29、sportmaster-ui、ux-commercial-inputs、sportmaster-window-lifecycle | 删除独立历史入口。当前识读反馈、投递位与窗口回归按当前轮次展示约束执行。SnapshotProbe、InputFactory 等仍在用的夹具已抽出保留。 |
| site-capabilities、quality-extensions、inventory-closure、signed-*、qualified-capabilities、reliability-closure、audit03-input、audit03-maintenance、startup-deployment | 清除未被调用的旧可选登录、称重、外置签名模块、旧格式盘点和旧部署组合。startup-deployment 实测首个断言仍要求旧可选库存呈现。当前 DB 格式、安装事务、隔离恢复和合约拒绝保留专门测试。 |

### 参数数字的复核

| 来源 | 数字和用途 |
|---|---|
| MvsPresenceChecks | 运行移出使用至少三帧、300 ms。799/800 ms 属于显式校准；另一个 800 ms 样例验证保存后的覆盖值。 |
| RcsHeartbeat、RcsTimeoutErrors | 500 ms 心跳周期、250 ms 紧凑探测、1000 ms 新取格口期限属于三种时钟。读取操作员投影会隐藏冻结的连接参数，期限断言改走授权的原请求读取。 |
| CameraRoundHarness | 合成数据中的识读、请求和心跳预算由测试指定，以便单独观察识读或发送责任。这些值不能作为出厂默认值的证据。 |
| BackupConsistency、LifecycleRestore | 复制回调和恢复确认各有维护守卫。必须等进入对应阶段，恢复后的 Resume 需要最新设备健康事实。 |

### 删除和提取的旧源文件

以下文件名均相对于 tests/Ipc.Application.Verification。编号核心套件的迁移见清洗报告中的归属表。

| 原文件 | 当前覆盖或保留内容 |
|---|---|
| UnreadPackage27.cs、OperatorCorrections29.cs | ReaderFeedbackChecks、ReaderRobustnessChecks、RepeatedPlacementChecks 和原图用例检验当前帧、同件防重以及左右独立状态。 |
| SportmasterUi.cs、UxCommercialInputs.cs | 保留被 CommercialQuality、LatencyProjectionChecks、ParameterRefactor 等直接使用的 SnapshotReadFixture 和 ReferenceInputFixture，移除两个未注册的测试主体。 |
| SportmasterWindowLifetime.cs | 旧窗口组合的独立入口已移除。当前 WPF 组合通过 Desktop.UiVerification 和独立 Operations 进程验证。 |
| OptionalCapabilities.cs、SignedCapabilities.cs、ArchitectureQuality.Extensions.cs、InventoryClosure.cs、ReliabilityClosure.cs | 移除无人调度的旧称重、可选扩展、旧部署资格及其专属夹具。运行时身份拒绝、当前格式检查、签名证据及安装入口另有独立测试。 |
| Audit03.Inputs.cs、Audit03.Maintenance.cs | 从旧称重与 PLC 组合提取仍由工作流使用的 SM 快照场景和必要夹具，见 SnapshotOrderingChecks。 |
| StartupDeployment.cs | 对旧可选库存目录的第一条断言已失效。现行首配、缺少设备、授权及安装配置分别由 sm-configuration-preferences、mvs-offline-activation、runtime-identity-boundary 和 installation-configuration 对应领域检查。 |


### 按现行契约复核的工作流断言

| 入口 | 原假设 | 回归依据 |
|---|---|---|
| 海康 SDK 诊断 | 运行日志会隐藏条码和所有候选 | 现行诊断需保留条码、候选和 SKU；密码及令牌须脱敏。实时尾部、轮转前原文件、授权查询、上下文和导出逐项核验。[调试日志契约](../../runtime/debug-log-live-01.md) |
| SM 快照顺序 | 忽略旧实例或倒退修订后会立即使当前状态失效 | 已接受的对象必须保持原样。另注入超期发布时刻，验证失效提示和操作守卫；迟到读取、暂停与恢复读取仍分开测试。 |
| SM 请求顺序 | 任意 HTTP 都算取格口，进程重开后同车能直接再交一次 | 只计实际取格口报文，等到本位相机边界就绪，再在原轮次之后明确换车。检查独立工位的提交顺序、冻结编号和备份后的高水位。 |
| 双路 TCP 模拟 | 同一放置换码能当作另一件，启动失败的 SM 输入会写入旧人工中断事件 | 在空闲右位验证暂停拒绝；从业务输入记录检索原条码。启动失败或取消必须跳过原排队输入，数据库保留其归属并且没有新外呼。 |
