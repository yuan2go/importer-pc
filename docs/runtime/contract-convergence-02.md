# 现行合同收敛 02

工作包：[CONTRACT-CONVERGENCE-02](../work-packages/WP-IPC-CONTRACT-CONVERGENCE-02.md)。起点 `e165e490`。代码与测试都服从当前合同；历史记录不转为新源的通过证明。

## B1：SM 装配与交付

删除 SM 的 MvCodeReader/纯采图构造与目录条目、旧观察组合和 signed 扩展兼容组合，兼容声明从24组收敛为1组；移除 MvCodeReader 的 SM 发布依赖、打包器和3条专用Python测试。生产 SM 只接受内置 MVS 软件识别，旧输入在创建资源前拒绝并保全。当前兼容声明同时允许显式 Simulation 的手工输入与参考组件，用于机制验证，不是生产回退。

保留独立 SDK 工程、其 ABI/原生资源验证、其他业务扩展及 TCP。共享 TCP 环回场景仍验证启动屏障、同码不同实体、左右位隔离、旧回调隔离、配置 CAS 和原请求；SM 的生产入口和可选目录不接受 TCP。`DualCameraCommunication` 中“重启后始终暂停”的过期断言删除，配置只在受控重启启用和原请求不变断言保留；自动恢复规则由现行 `MvsOfflineActivation` 覆盖。

本机 Application 及 SM WPF locked Release 构建通过。定向检查覆盖内置目录/外置拒绝/未构造不得准入/旧相机拒绝、版本声明、SDK编辑模型、单相机、MVS软件识别及离线启用。MVS链使用真实ZXing-C++、真实SQLite和环回HTTP，原生采集端为替身。厂商真实SDK、物理相机、客户RCS、现场验收均 NOT_RUN。

Python全量发现首次执行101项，13个失败、2个错误：旧启动脚本夹具假设已被替代的运行预检调用顺序、把POSIX文本伪装为Windows EXE，以及按源字符串定位已移动实现；另有普通令牌不能创建符号链接。原日志保留于D盘 `.artifacts/contract-convergence-02/b1-python.log`，不称全仓全绿；后续批次按合同处理启动夹具，符号链接权限保持原门禁。

## B2：退休测试与重复分支

删除 `MvCodeReaderFlow` 及其替身、SM signed RCS运行流程、固定旧版1.2.1 signed心跳流程和相应四个执行入口。旧命令明确拒绝，不能误落入默认验证。`Audit03.Rcs` 删除signed开关与设置分支，保留同一真实SQLite/HTTP路径的全部原编号、备份恢复、原件拒绝断言。验证工程不再依赖MvCodeReader工程；独立SDK验证仍保留。

Application locked Release构建通过；`audit03-rcs`、`rcs-heartbeat`、`rcs-timeout-errors`、`mvs-software-flow`、`mvs-offline-activation`、`storage-schema-only` 全部退出0。三个同码独立识别/请求、Unknown不重发、配置离线启用、健康/恢复以及非当前格式原件保全继续通过。没有把signed成功断言改为允许外置成功，也没有把厂商SDK替身当真实SDK验收。

## B3：启动与身份验证收敛

旧Python夹具将带shebang的文本命名为EXE，无法在Windows执行，并断言旧Run/Install子进程序列。改用本机编译的无副作用原生子进程，按当前登记根、桌面直接启动、Console/Diagnose预检及独立安装/离线维护入口验证。合并为3组分派行为测试；供包与Inspection的原身份检查保留。Parcel源码字符串顺序测试删除，替换为三业务实际StartAsync拒绝：无配置解析/标准化/设备访问，无新数据库/租约，原输入及文件集合不变，并核对生产默认仍为原机级身份检查。

Windows扩展构建边界改用真实目录junction验证越界链接拒绝，避免要求额外符号链接特权；POSIX仍使用符号链接。未跳过测试、未提升权限、未改机器安全策略。最终Python全量97项通过；Application locked构建和三个身份行为检查通过。首次失败日志继续保留，不倒记成通过。

## B4：历史查询只保留可取消会话

删除Parcel公共契约和实现中的不可取消QueryHistoryAsync/HistoryMetricsAsync桥接，移除测试替身的对应转发。现行UI早已要求IParcelHistorySessionApplication，所有本轮测试直接调用可取消的ReadHistoryPageAsync；Lifecycle同一历史页重复读取合并一次，原断言保留。Supply同名方法仍有生产入口，不删除。公共契约仍按实际DLL字节检查，所有当前组合重编译，不改标或覆盖冻结包。

长稳测试原先要求恢复后全局Paused，已被按位隔离及SM自动启动取代。改为断言有未清输入的left位不能准入，保留原请求Accepted/Attempts=1、两条Interrupted和检查后责任清偿/无新实体；随后显式进入维护暂停屏障进行原件检查，不绕过其授权。Application构建、history-session-contract、reliability-02、stability-audit-history及完整win11-stability通过。一次构建与验证进程争用输出DLL导致复制失败，待进程完成后串行重建通过；不计为产品测试通过。

## B5：操作入口与文档收敛

删除仅生成SM外置RCS配置的configure-sportmaster-rcs.py，其输出已被SM内置合同拒绝。AGENTS、INDEX、framework/F决策和关联操作指南整理为现状；旧说明按原字节归档并核对SHA256，原工作区AGENTS另存副本且未改原文件。F编号、D/RV、既有验证记录及archive不变。SM发行说明不再附带TCP和MvCodeReader当前操作指南。发布文档与组合检查各3项通过，当前文档内部链接和归档原字节检查通过。

## 保留与不确定项

| 单元 | 判断及依据 |
|---|---|
| 原请求/Unknown、原始回执与接收责任、SQLite拒绝保全 | 现行主流程必须；RCS审计、存储格式、历史/恢复与长稳实测保留 |
| 唯一配置账、CAS、管理模式、运行身份及真实资源释放 | 生产守卫；本轮保留机制，测试直接走实际启动、真实SQLite/HTTP或受控故障边界 |
| DirectPlc/WcsToPlc、Supply/Inspection、通用TCP和外置扩展 | 独立业务或共享生产机制，SM不用不构成删除依据；DirectPlc/WcsToPlc重新构建通过 |
| 独立海康SDK工程、ABI/原生句柄检查 | 外部SDK合同；只移出SM交付闭包，不删除SDK协议实现与对应验证 |
| retired身份/编号表、当前完整恢复及原根退役 | 保障原责任，名称含迁移/退役不等于旧格式兼容；保留 |
| 共享扩展构造/解构重载、LegacyNamedCapabilities等名称 | 未能证明外部加载和其他业务入口已失效；保留，不按名字或无仓库引用删公开边界 |
| Supply/Inspection源码顺序检查、跨模块资源/Unknown重复检查 | 尚未证明场景与故障阶段等价；保留。不能把所有相近断言合并为一个弱断言 |
| 维护暂停屏障与历史测试中的Pause调用 | SM取消作业按钮和保存前置不等于删除共用维护守卫；本轮仅替换已证伪的全局暂停结果断言，其余保留 |

本轮完成上述候选的合同追溯，不宣称已经逐行证明全仓70,359行或所有保留测试唯一且不可再简化。未证实等价/不可达部分按用户要求保留，外部协议和其他业务不纳入推测删除。

## 计数与边界

计数范围为受Git跟踪的src/tests/scripts下.cs/.xaml/.py/.ps1，按物理行计，排除生成目录与历史证据；JSON/项目文件另计。C#工程没有统一用例登记，检查调用点包含辅助声明和循环，不能冒称独立测试数。

| 指标 | 前 | 后 | 减少 |
|---|---:|---:|---:|
| 代码文件 | 614 | 611 | 3 |
| 代码行 | 70,706 | 70,359 | 347 |
| C#验证检查调用点 | 3,691 | 3,677 | 14 |
| Python test_方法 | 104 | 97 | 7 |
| SM兼容组合 | 24 | 1 | 23 |
| SM兼容JSON行 | 1,710 | 59 | 1,651 |

最终Python全量97项通过；各批累计17个不同命名C#验证入口成功，具体源码批次、输出条数及日志hash见[验证摘要](evidence/contract-convergence-02/verification.json)。Application及SM WPF、桌面验证工程、DirectPlc/WcsToPlc locked Release构建通过。新SM publish目录启动business-identity成功，实际业务为sportmaster，目录不含Ipc.Drivers.Hikrobot.dll；这是发布程序只读身份冒烟，不是完整安装包或真实SDK启动验收。

扩展执行的lifecycle-ux中，LifecycleOptimization的历史、原请求及离线维护检查通过；随后MaintenanceProgressChecks进入实际备份复制时被RequireExtensionInstallerAuthority拒绝（普通Windows令牌），整个命令退出-532462766。该组结果为PARTIAL/环境受阻，剩余提权备份取消流程NOT_RUN；未删断言、未绕过门禁、未申请提权或改机器策略。其余成功项不能覆盖这个缺口。

批次提交：B1 `cd6b7e76`，B2 `0f0ce42d`，B3 `9eaa2d58`，B4 `efafacc6`。B5包含本报告、当前指引、原字节归档及manifest；最终PR另附实际分支提交。

复现先在Windows执行 `. ./scripts/project-environment.ps1`，再运行 `dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false`、`python -m unittest discover -s scripts -p 'test_*.py'`，C#按 `dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --<入口>` 执行。维护提权检查必须另在获授权的隔离安装环境完成。日志位于工作区忽略目录 `.artifacts/contract-convergence-02/`，摘要记录其实际hash，不包含SDK、生产数据或凭据。

安装生命周期、目标工控机原生交互、真实SDK/设备、客户RCS及现场均 NOT_RUN。CI保持停用；不生成发布安装包、不操作已有安装或运行数据，不改D/RV。
