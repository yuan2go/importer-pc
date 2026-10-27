当前配置仅schema3，Parcel数据库仅10，Supply仅6/6；其他格式拒绝并提示使用全新数据目录。程序集版本与包身份以当前项目声明和本次构建清单为准。旧指南原字节及形成时证据索引见[收口前归档](../archive/pre-fresh-baseline-current-guides-01/README.md)，不用于当前操作。

# AQA01 架构质量整改 · 当前实现与验证入口

用户授权在独立分支整改 AQA01-F01～F06，基线 main `0c5d7d25e572e80f23522c9ff7b562db920628a3`（PR91）。原只读报告及失败记录保持原身份；本包不合并、不发布安装包、不操作生产设备。范围与验收见[工作包](../work-packages/WP-IPC-ARCHITECTURE-QUALITY-REMEDIATION-01.md)。

## 输入与显示（F01）

`ParcelRuntime.EnqueueCore`为入队观察记录接收时间，`ObserveAsync`区分三类事实：

* 进入时已经陈旧、未来时间、错误注册/设备/序号/事件身份：只审计，不推进有效序号，不覆盖当前输入。
* 进入时新鲜、处理时因本位排队超时的新实体：写既有`InputObserved`账为`Rejected / ObservationExpired`，不创建作业或请求。有效注册的当前输入遇到健康失效写`Blocked`。
* 已有实体按原冻结条码策略核对重复/冲突，原请求、未知结果、资料和原图责任不清除。

`DisplayedOperation`及既有Sportmaster/共享页面投影消费同一输入事实，上一终结格口不能借给本次失败输入。未结原作业继续作为独立责任显示；位间调度、位内顺序和软件暂停语义不变。接收责任现按[稳定性整改设计](../design/win11-24x7-stability-remediation-01.md)在同一业务账持久化；接收、内存排队、业务Admitted及完成分别核对。AQA01形成时仅RAM接收时间的原说明见[归档](../archive/pre-win11-24x7-stability-remediation-01/README.md)，原测试结论不扩大。

## 命令持久化（F02）

复用`parcel_commands`和SQLite事务，当前基线直接包含完整结构：

|操作|持久化及重启规则|
|---|---|
|Pause/Resume/过载/身份冲突确认|原业务事务同时保存命令hash和回执|
|AbandonUnsubmitted|Abandoned、未发送意图取消及命令回执同事务；随后原有持久释放/清理流程处理原图，崩溃不丢命令身份|
|ContinuePrepared / ContinueUpload|发送前持久保存本次继续准入和命令回执；回执证明准入，不证明外部接受或物理完成。重启仍执行原件恢复屏障，不因重放命令自动发送|
|ArchiveHistory|归档事实、永久身份及命令回执同事务；无候选时也保存核查回执。冗余原件文件在事务后清理|
|ConfirmConfiguration / ConfirmRestore / ReconcileReceipt|跨文件或外部查询前先保存命令身份及Pending回执；完成时保存终结回执。中断则保留Pending，同ID返回该回执而不重新执行；快照AuxiliaryReasons显示CommandPending，沿CommandAdmitted审计核对原配置/交接/Query/资料责任，再以新命令显式处置；暂停排空并进入管理模式后，可用CloseInterruptedCommand（Owner=原Pending命令ID，Value=检查说明）记录Interrupted，不清除任何原业务守卫|

同ID异内容始终先于CAS/业务守卫拒绝。显式检查仅将Pending转为Interrupted并保留新检查命令/管理证据。已完成回执不可改写；已返回的终结回执与后续重放一致。进行中请求可先读到Pending，最终回执按实际完成更新；Pending不是成功或外部未知结果已解除。中断身份不会自动恢复执行，不存在网络长事务。原冻结报文、目标、请求ID、发送尝试和回执资料责任保持。

当前命令ID、内容和接受事实由原事务持久化，不伪造补账、不重发未知请求；从零建库直接包含当前接收责任结构。

## 离线引用与管理投影（F03、F04）

`ParcelRepository.ValidateConsumerConfiguration`与装配入口一致识别内置`manual`和`tcp-line`；它们不伪装为扩展实例。Effective、Pending、原作业冻结配置及完整备份均走同一离线读取。扩展实例/设备/包的精确引用、未知驱动、未知迁移、损坏或缺失原件仍拒绝并保留包。平台仍只分派应用自己的只读消费者，不解释业务SQL；本包没有修改受保护扩展Store或现场登记。

`OperatorSnapshot`对Inputs.ReaderBinding应用与配置/作业/历史相同的参数遮蔽。进入管理模式才能读取原参数及请求；退出、到期或改密码后的普通读取仍保留操作所需条码/实体/阶段事实。持久原件不修改；参数读取边界不是操作员登录。

## 验证及入口维护（F05、F06）

`ApplicationArchitecture`使用真实管理模式接口，普通取消夹具遵守token；仅所有权超时场景模拟有界的不配合外部调用，所有出口finally释放。`ResourceRisks`在新场景前释放重启实例，保留Stop超时/机级设备租约断言。配置调用只重试明确的调用前守卫拒绝；不吞异常、不重试未知发送。

真实CLI在stdin提供`ConfigureManagementPassword`、`EnterManagementMode`、`ExitManagementMode`，转发同一`IWorkstationAdministration`及既有批准政策，无默认密码、无命令行口令。`smoke-application.py`仅在全新隔离根随机设密，重启重新授权后核原报文/Query；口令不进入证据、argv或产品配置，受保护现场仍须原批准服务。可选恢复夹具明确选择既有AtAssociation策略；新增UntilDispatch过期重量阻止发送验证，生产守卫不改变。

当前操作入口统一为从零安装工作包、运行入门及版本规则。历史整改与原Windows/现场记录见原字节归档，不转记本轮身份。

业务版本Sportmaster **1.11.2**、DirectPlc/WcsToPlc **1.5.1**，对应项目与compositions同步。公共扩展ABI仍为Contracts **1.16.0**，DB **8**；本轮没有新公共DTO。产物仍以SourceCommit与文件SHA识别，不混用整套发布程序集或覆盖旧不可变扩展包。

定向验证入口：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --architecture-quality-remediation
```

其他必要验证、命令、退出码及边界见[本轮验证记录](validation/architecture-quality-remediation-01.md)。运行证据为本机Windows上的合成数据、真实SQLite/loopback及解包进程；界面投影测试不是原生窗口人工验收，参考端点不是真实WCS/PLC验收。Windows目标构建、实际进程、安装/UAC/受保护Store、厂商SDK及现场分别计证。
