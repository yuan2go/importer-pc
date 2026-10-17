# UI-MAINTAINABILITY-16 实施与验证

基线main `12a40b308e13744eb4be959dff4a62dca473b5d9`，PR15已合并，开工无开放PR。原工作区未跟踪材料保留，使用隔离分支`codex/ui-maintainability-16`。前置文档提交`a0595ed`早于业务改动，原字节及替代关系见[前置归档](../archive/pre-ui-maintainability-16/README.md)。[有效基线/计划](ui-maintainability-16-baseline.md)与[工作包](../work-packages/WP-IPC-UI-MAINTAINABILITY-16.md)记录当前有效要求。

代码已实施，核心新增定向验证及WPF交叉构建已通过；本文件为实现冻结时记录。发布候选以同版release.json/PACKAGE.json/composition.lock.json及最终[checks](ui-maintainability-16-checks.json)登记实际源/包/hash为准，不将尚未运行的候选步骤预记PASS。Windows实际截图0、全部客户端旅程NOT_RUN；真实SDK/WCS/身份/实体/数量/逐SKU少货及现场批准仍按能力BLOCKED，Production NotReady。

|AC|实际实现/可操作路径|证据与边界|
|---|---|---|
|16-01|当前入口归档、16工作包/有效基线；实际读取规定设计、13～15任务/报告/checks/D/RV和UI实物、配置、脚本及调用链。|a0595ed；原SQL/checksum、历史报告/失败/原任务及v1.1原字节不改。|
|16-02|MainWindow.Alarms按原Source/Scope诊断无Owner报警，有Owner查原对象；历史单击选中、显式详情/控件内双击Enter、直接箱作业与关闭链、列表书签恢复。主屏互斥提示中性，技术证据移详情，结果/实际阻断/固定动作保持。|[UI映射](../ui/ui-maintainability-16-mapping.md)；交叉构建及源接线/策略验证，实际WPF行为NOT_RUN。|
|16-03|MainWindow.Maintenance与DeploymentPresentation显示草稿/保存/Pending/Effective/失败保旧；实际相同validator定位字段、schema旁错误/首错聚焦、草稿与有效对照。原统一草稿、选择代次、权限/CAS和后端生效守卫不弱化。|OperationClosure/Conformance受影响验证与UI映射；跨字段错误不能唯一定位时保留完整摘要，不猜字段。Windows双窗/HID/焦点NOT_RUN。|
|16-04|Adapters→Contracts，无Supply实现/SQLite依赖；Loopback验证自身边界；Product明确装配Supply，Fixture明确自有数据库依赖。SupplyCommands/Action/GuardAction、共享权限/箱资格、原因码、接收责任/SQL和所属视图定义接入业务/UI/Product。|ArchitectureMaintainability真实程序集/业务命令/SQLite判断验证；原wire/audit名称不变。公共契约已存在，未另建平台/完整第二业务UI。|
|16-05|build-extension声明独立工程/输入/manifest/modules/schema/content，隔离构建、签名、冻结宿主extension-check静态验收和独占发布；POSIX进程组/Windows Job拥有构建树。发布Python/PS统一含integration-reference，四份当前指南可离线使用；选择器真实帮助/标签。|工具边界/失败/取消测试；独立实际工程及候选结果以checks补充。Windows Job/PowerShell实际未运行。|
|16-06|健康同状态续期留在串行当前事实，状态/有效性/失败变化持久；后续业务事务保存最新确认，重启清空健康重新确认。观察/T1/T2/T3/接收责任未合并。|[前后测量](evidence/ui-maintainability-16/health-comparison.json)：360健康确认的提交区间/审计/state重写360→0，作业行720→0；快照P95约0.810→0.671ms。确定性虚拟时间/实际SQLite，不是现场性能资格。|
|16-07|必要locked restore/完整WPF交叉构建、health/command/adapter风险、原Q1/Q2/T3/唯一接受/原请求、CAS失败保旧和资源/旧包回归按影响执行。|原失败日志保留；最终checks逐命令/源/hash；没有Windows环境、不新开VM/runner/OAuth。|
|16-08|当前运维、首次作业、独立扩展、UI映射/Windows清单、manifest和同版候选；开发分支提交并创建main PR。|最终交付身份及PR由checks/报告追加；不以PR或百分比替代功能验收。|

## 健康写放大测量

同一Profile方法在修改前后运行：两个在位通道、两来源Ready及一个requests服务，120个一秒虚拟间隔，360次可靠确认全部入协调器。独立SQLite连接逐调用读data_version；审计增量及临时SQL触发器独立记录聚合UPDATE。稳定区间不含初始化/停止；实际业务观察未降采样。改前健康方法为main原实现，改后方法只取消无语义变化的全聚合保存；来源确认、有效期、epoch/顺序、失效和恢复不省略。快照时延受本机负载影响，不代表自然双源吞吐承诺。

新验证保护：每次时间/序列在当前快照可见、续期不干扰业务CAS、下一业务事务保存最新确认、相同时间/旧epoch和sequence不能续命、读取不能续期、失效转恢复持久、断开阻断、观察逐条保存、重启全部重新确认。存储状态变化失败仍按原StorageFailure阻断。无需格式变更，Storage/reader5/5保持，SQL1～5/checksum不改。

## 兼容与维护边界

共享公共DLL新增动作/规则/view声明会改变精确字节，因此参考扩展使用新不可变1.0.4。原1.0.0～1.0.3/登记/宿主保留，ContractMajor仍1；不以主版本相同宣称旧包满足新指纹。旧请求冻结原Package/Implementation/Contract/Binding/payload/hash，缺原能力明确阻塞。旧部署经匹配旧宿主清偿/合法过渡，再按当前[运维](operations.md)安装/保存/受控激活；本包不伪称重跑15旧部署旅程。

未拆公共契约程序集：已有Ipc.Supply.Contracts满足端口/DTO隔离，新增程序集会增加精确插件迁移而无本次收益。未移动StationConfig现有业务字段：会触及公共持久形状/旧请求，本包适配器校验自身边界已解决实际依赖。窗口按报警、维护、历史/导航职责拆分，未新建任意业务UI插件平台。Product/Assembly继续合法引用具体实现，所有窗口/页面共用唯一业务实例/来源/发送器。

## 运行与独立门禁

首次运行/首条参考作业见[当前指南](getting-started.md)，构建/定向验证/发布/排障/恢复见[运维](operations.md)，声明式独立扩展与安装配置激活见[扩展指南](../extensions/developer-guide.md)。portable核心只能验证参考链；win-x64候选需要实际Windows，[后验清单](validation/ui-maintainability-16-windows.md)逐项NOT_RUN。

解除生产依赖需：获授权SDK/设备型号固件位数与停止/回调合同，版本化WCS阶段/幂等/原查询/关闭及逐SKU明细契约，可信身份/权限，实体映射/联锁/复位和数量拆包批准，以及现场OS/账户/ACL/输入/DPI/声音/触控及恢复SOP。没有相应证据的能力保持隔离，不影响其余参考工程交付。D/RV和旧HTML实际资格不因局部实现/测试关闭。
