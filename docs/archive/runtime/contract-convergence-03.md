# 现行合同收敛03

基线 `c96cb382`，按[工作包](../work-packages/WP-IPC-CONTRACT-CONVERGENCE-03.md)继续核查02保留项。历史02报告、D/RV和验证记录不改写；CI停用，原工作区保留。

## B1：部署记录兼容重载

删除DeploymentExtensions双参数二进制构造器及双值Deconstruct。当前四参数记录的Services/Ports已有可选值，当前设备扩展和完整服务/端口部署仍由同一契约表达；本轮不删除这些能力。ExtensionContractCompatibility.Accepts及ExtensionPackages按实际Platform/公共契约字节拒绝旧包，不能用自有旧二进制兼容为桥接保留理由。原签名包及第三方SDK协议未修改。

现有ExtensionDraftVerification继续保护重命名、目标/设备/服务互斥、原候选不变及重复保存，增加同一完整候选与设备候选的JSON往返检查。独立--extension-draft入口运行同一个测试函数，不复制断言，也不启动无关HTTP夹具。Ipc.Verification与五种业务装配按锁文件Release构建，所有公共契约消费者须重编译，不借用旧包验证。

## B2：身份顺序检查改为行为检查

删除Python里Supply/Inspection运行入口的源码字符串顺序检查，保留独立维护入口的权限/返回分支检查。Ipc.Verification现有--runtime-identity入口同时运行实际ProductRuntime和InspectionRuntime：配置文件故意无效，注入身份拒绝必须先发生；Supply保持NotReady，Inspection保持异常；Dispose后原字节和目录文件集合不变，无数据目录/设备构造。生产默认仍是MachineRuntimePolicy.RequireRuntimeIdentity，注入仅为friend验证程序集可见的internal init属性，不新增部署开关或放宽策略。

Ipc.Verification locked Release构建、共享身份政策/两项实际入口检查和4项Python启动派发检查通过。验证工程增加Inspection项目引用，锁文件按当前SDK重算，没有包版本升级；全部产品依赖锁保持不变。原身份指南原字节已归档，当前入口说明新的验证边界，原Windows/ACL/系统安装记录不改写。

## B3：管理模式检查服从自动保存合同

删除OperatorAdministration中的两个过期检查：全模拟配置切换Production后期待旧认证异常；已授权SM仍因未手动暂停而拒绝保存。没有把异常类型改宽来凑通过，也没有把错误的拒绝断言改为保留旧分支。首次基线在前一检查失败，实际异常来自现行SM相机准入；原日志保留。

生产旧配置拒绝由sm-built-in-only保护，当前不暂停自动等待/保存/Pending/Effective由mvs-offline-activation保护。management-mode完整入口通过，包括撤权、过期、一次性授权、原密文保全、参数读取与保存权限、日志及异步授权代次。mvs-offline-activation、sm-built-in-only、runtime-identity-boundary均通过。原请求/Unknown及存储只读拒绝另外由audit03-rcs、storage-schema-only复验通过。

## B4：真实进程退出测试按就绪事实同步

第一次Python全量96项中95项通过，test_failure_stops_real_descendants未观察到后代写入标记，因而未能建立释放测试的前提。夹具固定0.25秒后退出，允许退出早于后代就绪。取消/失败两条测试仍分别验证不同退出原因下的资源责任；共用的真实子进程夹具改为显式触发失败文件，测试观察后代确实写入后才发信号/触发失败。所有后代停止后才允许owner返回的断言全部保留，生产build_process.py未改。两项定向行为检查通过。

## 02保留项的核定结果

| 保留项 | 当前合同与入口，结论 |
|---|---|
| LegacyNamedCapabilities | 文件内的WeightPolicy/CapabilityBinding/OperatorIdentity仍用于Parcel重量守卫、冻结请求、适配器和操作员会话；如ParcelGuards.WeightReady、ParcelIntegrationIds、ReferenceWcs。保留类型、命名空间及协议身份，不把旧文件名当作旧实现 |
| 其他构造重载 | ConfigurationRevisions三参入口选择无持久化回调；RuntimeJournal选择默认时钟/保留策略；ResourceLease两参公开入口与内部不建目录入口职责不同；LocalManagementMode普通根与System服务授权不同。均有当前生产机制，不合并成绕开守卫的默认值 |
| Supply/Inspection源码检查 | 运行身份顺序已合并到真实StartAsync拒绝；Supply独立backup/preflight/restore结构检查保留。其机级维护权限与日常身份不同，不能由普通运行测试推出等价 |
| 维护暂停及Pause测试 | ParcelCommands的原回执核对、输入责任检查、一致性备份仍有暂停/排空守卫；SM SupplyStationWindow.OpenRetentionOwner入口仍显式进入屏障，其他业务保存也仍使用。只删除SM保存前手动暂停的旧结果断言，不删除维护屏障或其行为检查 |
| retired实体/测量记录与编号范围 | ParcelHistoryRetention.RetiredIdentity/RetiredMeasurement在清理已结详情后保留永久身份；ParcelIntegrationIds.NextIntegrationPreparationSequence读取原分配/恢复下限，防止重用请求编号。是当前防重/恢复机制，保留 |
| 原请求/Unknown相近测试 | Custody05覆盖原始上传文件已刷盘而T3未提交的真实进程退出；Recovery02覆盖主结果已接受、附属上传Unknown及查询健康围栏；Audit03覆盖RCS编号与原目标。故障阶段、证据和资源责任不同，不按同名Unknown合并 |
| 资源释放相近测试 | 驱动清理失败不能自动重开、应用停止不能提前放租约、构建进程失败/取消不能留下后代，属于不同所有者和退出边界。保留；B4只移除固定时延假设 |
| 独立SDK、共享TCP、其他业务与扩展 | 第三方ABI/协议和DirectPlc/WcsToPlc/Supply/Inspection仍有独立范围；本轮不删除、不改外部方法或原签名包。公共DLL变化后依现行精确身份规则重编译消费者，不接管旧二进制 |

仍未确定：Supply离线维护在实际受保护安装中的完整控制流，需要获授权Windows安装环境；此轮只保留源结构检查，不宣称行为验收完成。未逐条证明所有跨模块断言等价，也未运行全部非SM/厂商SDK套件；不能凭引用少或标题相近进一步删除。

## 最终验证与计数

- 8个不同命名C#入口通过：extension-draft、runtime-identity、management-mode、mvs-offline-activation、sm-built-in-only、runtime-identity-boundary、audit03-rcs、storage-schema-only。它们按各批实际源码运行，不能把PASS输出行数当用例数。
- Python最终全量96项通过；首次全量95通过/1失败的日志保留。B4修复就绪同步后重跑全量，未跳过失败项。
- Ipc.Verification、Ipc.Application.Verification及五种业务装配locked Release构建通过；Supply/Inspection在身份边界调整后重编译。Supply与SM WPF构建通过。SM新publish目录business-identity只读启动通过；不代表完整安装包或设备启动。
- 首次误把仅Parcel支持的business-identity参数传给Supply装配，入口按现行合同拒绝Unknown assembly option；未将它当作Supply运行失败，也没有为该错误命令新增兼容分支。
- 当前文档链接、归档原字节/SHA256已核对；最终提交同时执行canonical manifest和diff检查。实际日志hash见[验证摘要](evidence/contract-convergence-03/verification.json)，原日志位于D盘忽略目录.artifacts/contract-convergence-03。

| 指标（与02相同口径） | 前 | 后 | 变化 |
|---|---:|---:|---:|
| src/tests/scripts指定文本代码文件 | 611 | 613 | +2 |
| 代码物理行 | 70,359 | 70,407 | +48 |
| C#静态检查调用点，非独立用例数 | 3,677 | 3,677 | 0 |
| Python test_方法 | 97 | 96 | -1 |

净增代码来自实际运行边界测试和测试专用的internal注入入口；本轮以消除错误合同与保留强行为证据为目标，不以负行数为目标。未增加业务功能或部署权限开关。

构建/复跑先执行 `. ./scripts/project-environment.ps1`。`dotnet build tests/Ipc.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false`后可运行其DLL的`--extension-draft`、`--runtime-identity`；Application验证按同样构建后使用 `dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --<入口>`。Python为 `python -m unittest discover -s scripts -p 'test_*.py'`。

厂商SDK/物理相机、客户RCS、原生WPF交互、系统安装/修复/卸载、提权备份取消及现场验证均NOT_RUN；02的环境受阻结论不因本轮通过而关闭。CI保持停用。四批提交为 `5e30bcb5`、`c9b6e1b3`、`d009c441`、`6631c04b`，最终文档/manifest提交与main合并身份由PR记录。
