# UX商业质量修复 Implementation Plan

> 开工计划及原核对项保留；已执行结果与剩余验收见[修复结果](../../runtime/ux-commercial-fix-01-results.md)，不将下文计划清单当作当前未实现功能。

> For agentic workers: execute the approved WP-IPC-UX-COMMERCIAL-FIX-01 task by task. Use separate file ownership for UI and maintenance work; root owns application facts, integration, verification and publication.

**Goal:** 修复UXC-01～07的可实现部分，交付同一Application的真实页面和可运行媒介，Windows/现场验收后置。

**Architecture:** Application持久观察事实进入既有审计，桌面只投影。schema3编辑复用原配置CAS和Store。服务IO异步执行，UI更新仍属窗口；不改变原请求或主流程。

**Tech Stack:** .NET10.0.402、WPF、SQLite、现有Python选装发布工具。

## Global Constraints

- 三个独立Application；Sportmaster双位扫码→WCS格口，无PLC/图库；DirectPlc完整UI暂缓。
- 无生产设备操作；原wire/合同/目标/hash及Unknown不改；SQL1～7不改；DB7。
- 计划业务1.3.0、Contracts1.9.0和新不可变参考包；原设计/历史证据保留。
- 用户已授权修复，当前只做必要开发验证，正式Windows/现场验收另行执行。

### Task 1: 持久观察与当前结果

Files: src/Ipc.Application.Contracts/ParcelInputs.cs、ParcelContracts.cs；src/Ipc.Application.ParcelFlow/ParcelInputs.cs、ParcelRuntime.cs、ParcelGuards.cs、ParcelCommands.cs；tests/Ipc.Application.Verification/UxCommercialInputs.cs。

Consumes: 原Observation、ParcelSnapshot.Positions/Operations、repository.Commit及原输入队列。
Produces: `ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome, string Reason, string? OperationId, DateTimeOffset RecordedAt, DeviceBinding? ReaderBinding=null)`；快照末尾可选`ParcelInputSnapshot[]? Inputs=null`。

- [ ] 在真实runtime输入成功→NoRead后断言`Inputs.Single(x=>x.PositionId=="left").Outcome==Rejected`、CurrentOperation不再指上件成功；原Request/hash/attempt不变、右位不变。
- [ ] 运行`dotnet <verification.dll> . --ux-commercial-inputs`并保留RED。
- [ ] 按WP定义实现来源围栏、审计原件、最新位投影及重启重建；不伪造Operation。
- [ ] 复验空码/多码/控制字符/旧epoch/重复、旧未决保留及重启；运行既有audit03-input/application-architecture/sportmaster-rcs受影响组。
- [ ] 定向修复通过后提交。

### Task 2: 原生UI状态与页面

Files: Sportmaster.Desktop的SupplyStation窗口/位控件/主题/VM、ParcelFlow.Desktop的Shell/History/VM/Targets，Desktop启动页。
Consumes: Task1 Inputs、原Fresh/CurrentAt/Availability/CAS；Produces: 真实语义状态、800×420内紧凑布局、历史加载/空/失败、启动进度与健康证据范围。

- [ ] 先读v1.1完整实物与批准alternate设计，保护原资源。
- [ ] 保持正常/接受/拒绝/Unknown/失效不同语义；用Inputs解释本次观察，旧请求仍按原Current/Selected身份发命令。
- [ ] classic初始页加载；历史时间与原请求层次，完整JSON为高级详情；单调代次保护迟到查询。
- [ ] 真实紧凑布局保关键字段/动作，低于最小区域仍受控Pause/人工确认。
- [ ] `--sportmaster-ui`、窗口生命周期与必要WPF构建；不将shim当原生验收。

### Task 3: schema3维护

Files: CapabilityEditing.cs、MaintenancePage.xaml/.cs、新StructuredConfiguration.cs及定向验证。
Consumes: 原CapabilityCatalog、ParameterField、完整候选和加载时CAS；Produces: 全拓扑表单候选，原StageConfiguration入口不变。

- [ ] 为默认schema3及可选双秤/相机加载表单，不清空；提供者与原绑定精确匹配，未知提供者明确阻断。
- [ ] Build候选保全部位/reader、无关能力、Extensions及Retention；局部校验不保存，保存才用原CAS/原因。
- [ ] 修改一位后另一位/可选策略不丢；双窗旧草稿冲突；高级JSON显式重载不隐式覆盖。
- [ ] 安装/恢复/卸载使用真实清单和原Store身份；不可确认原操作时保持诊断，不猜身份。
- [ ] 运行结构化编辑、配置确认、库存与维护定向组并提交。

### Task 4: 服务IO与生命周期

Files: 桌面服务调用帮助类及相关VM/关闭流程；不得改变业务序列或创建第二runtime。

- [ ] 用受控同步阻塞服务建立反馈：调用后UI上下文能继续，集合只在UI投影阶段更新。
- [ ] 将纯服务调用而非整个VM动作置于被等待的后台任务；跟踪刷新/命令直到关闭排空或保资源失败。
- [ ] 验独立Pause、失败Busy恢复、挂起关闭/重试、选择/草稿不丢；不以WaitAsync超时冒充底层停止。

### Task 5: 交付

Files: 项目/兼容声明、运行/UI指南、manifest、原package-application入口及本包结果。

- [ ] 同步业务/ABI/新精确包身份，测试旧版本事实与当前声明分开。
- [ ] 使用固定SDK 10.0.402的`dotnet`，串行`-m:1 -nr:false`执行必要模块/恢复/配置/资源测试和三套WPF交叉构建。
- [ ] 提交冻结源码后生成实际portable和Windows候选；选装签名/依赖/Files/hash及解包参考启动；无私钥/SDK/字体/客户原稿。
- [ ] 记录源码SHA、命令/返回码、候选hash、原失败和未验边界；文档/manifest校验后推送当前开发分支并创建main PR，不合并。
