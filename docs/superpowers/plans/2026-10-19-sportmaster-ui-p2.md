# Sportmaster UI P2 Implementation Plan

> For agentic workers: execute the four approved review fixes in this worktree, with regression checkpoints before publication.

**Goal:** 修复PR27独立审查的4项P2，保持业务实现不变，更新同一PR，不合并。

**Architecture:** 共享桌面VM新增快照读取扩展点，classic默认仍直接调用原应用；新UI统一处理所有读取失败。布局保护仅调用既有Pause，空间恢复和人工确认不自动Resume，不改变作业、原请求或业务守卫。

**Tech Stack:** .NET10.0.402 / WPF；现有跨平台Application验证及Windows原生烟测。

## Constraints

- 起点PR head7d47a19、main baac260；原HTML不上传，历史回执保留。
- 不改业务Flow、Runtime、公共应用契约、SQL、驱动、协议或配置。
- Windows原生运行NOT_RUN；真实SDK/设备/现场BLOCKED，不能用交叉构建代替。

## Tasks

- [x] 1. `ParcelViewModel.cs`增加`protected virtual Task<ParcelSnapshot> ReadSnapshotAsync()`；Refresh、Pause及`ParcelTargets.cs`确认配置的直接读取全部经此入口。`SupplyStationViewModel`重写该入口，统一代次/失败标旧；`RefreshDisplayAsync`只转发Refresh。失效ReadStatus持续包含UpdatedAt。
- [x] 2. `SupplyStationWindow.xaml`诊断区限制高度、局部滚动，时间单独常驻；保持双位固定动作栏。最小窗口降至640×420，按根Grid有效客户区1280×680判断保护；覆盖层提供最大化、重试暂停、空间恢复后的人工确认。恢复不自动Resume。
- [x] 3. VM增加`SetViewport(double,double)`、`AcknowledgeViewport()`、`CanUseWorkspace`等本窗展示守卫；窄屏锁存后必须取得暂停快照并人工确认。窗口在进入保护时调用既有Pause，主窗关闭同时等待在途保护Pause，沿用失败保留和重试。
- [x] 4. `SportmasterUi.cs`回归真实命令后读取失败、Pause首次读取失败、配置确认读取失败、命令/轮询晚失败排序、最后更新时间、尺寸边界/锁存/无自动恢复；用真实Application核业务不重建/不新增请求。Windows烟测补50条诊断和长原因的布局测量、固定动作可见、小客户区保护与恢复及主辅窗口生命周期。
- [ ] 5. 执行UI及受影响架构/配置回归、Sportmaster与共享桌面兼容构建、Windows烟测交叉构建、文档/资料校验；冻结修复源码，生成新候选并解包验证。分别保存审查、修复与未执行边界；更新manifest、提交推送PR27，不合并。
