# WP-IPC-APPLICATION-CONVERGENCE-17

来源：2026-10-18用户“Application 架构收敛与场地业务贯通”八项完整指令。执行时 main `6e9c890a7483c3006b3448c203570ef2ca19ed25`，无开放PR；原工作区未跟踪客户资料保留，独立工作树/分支。用户授权根据现有代码决定方案，前置文档独立提交后直接实施。

## 有效设计与替代关系

继承F-02 .NET10/WPF/SQLite、ModuleCatalog、ResourceLease、受控签名扩展安装和精确AssemblyRef政策。保留Supply/Inspection既有行为、5/5数据库及原SQL/checksum、D/RV/REQ/BR/CAPI与公开边界。16的“当前任务”和“本包不移动配置字段”仅限16，不限制17；框架原“称重/PLC仅预留”被本包显式参考实现要求替代，真实设备仍按资料门禁。Supply双相机、箱、数量、requests合同只约束Supply。

方案比较：整仓改名并改持久身份会破坏原请求/扩展；另建平行框架会重复安装/生命周期机制。采用职责内聚的源码分组、显式兼容身份和增量契约，复用基座，新增独立Application装配；不建立第二个扩展加载器或隐式热切换。

Platform提供不含WCS/箱/数量的配置修订与校验流程、SQLite连接/有校验和的迁移执行/备份、生命周期。Application拥有字段/校验/生效屏障、SQL/仓储与事务/恢复。装配只选择策略、工厂和所属UI。新契约不引用WPF、具体适配器或业务数据库。

Sportmaster：读码实体身份→WCS格口→展示；不创建PLC依赖。DirectPlc：读码→重量策略→PLC；不创建WCS依赖。WcsToPlc：读码→重量策略→WCS结果与PLC意图原子关联→PLC。每个Application独立配置、数据根与流程策略，窗口共用运行实例；视图/布局与构建组合实际绑定。

重量：Disabled不发送重量；Configured明确数值/单位/精度并标配置补全；Measured由测量ID/同一AdmissionId、设备、时刻、质量、稳定性关联，拒绝缺值、过期、错误单位/精度、重复测量跨实体。未知不等于零，件数不作为重量。等待测量的业务不占用设备回调。

阶段：Prepared持久意图→Dispatching持久尝试→传输/协议确认/业务接受/物理完成独立。出站前冻结请求ID、Application/实体/阶段、包/实例/协议/原目标/配置hash/实现hash/报文/hash。重启Dispatching转Unknown；Prepared也保守进入待核对。WCS已接受而PLC未知，原格口和原PLC命令继续保留；查询缺失不等于未动作，不重发。重复/迟到/矛盾回执归原阶段，冲突隔离，不覆盖终态或计重复动作。

设备回调只TryWrite有界队列；满载明确拒收并锁存阻断。通讯工作异步有时限，取消不证明未交付。Stop拒新、停止源、排空/取消工作、等待退出，再释放端口/包/数据库/资源；未退出保留所有权。Production缺必需真实能力拒绝，不回退模拟。

## 兼容映射（编码前冻结）

|身份|17处理|
|---|---|
|源码项目/目录|Application单数；Supply/Inspection归Application；混合Adapters分Drivers/Integrations，宿主/装配/契约保留。|
|已发布Ipc.Supply / Ipc.Supply.Contracts公共类型|兼容命名空间/程序集名保留，源码职责路径映射；不以机械改名更改序列化身份。|
|已发布Ipc.Adapters|兼容入口/类型转发；新实现按Drivers/Integrations分离。公共DLL内容变化产生新包版本，旧签名包不覆盖。|
|StationConfig及旧Product入口|作为Supply兼容形状保留；新通用配置修订不要求该形状；业务校验归Supply。|
|SQL1～5 / 旧数据 / SIMULATION-v1 / 原请求|原字节/合同/参数/目标保留；新场地独立业务数据库及追加迁移，不用新场地库读取旧Supply库。|
|旧包ID/version/签名/HostContractSha|不改写；保留匹配旧发布与原包恢复路径。缺原实现明确阻塞；不放宽加载器。|
|新端口公共契约|独立不可变程序集/契约版本；精确指纹绑定新扩展版本，原加载安全规则仍适用。|

## 验收与必要验证

|AC|交付退出条件|
|---|---|
|17-01|文档/调用链/UI实物/D/RV/公开范围/最新main与PR核验、原字节归档、本包及兼容映射；前置独立提交早于代码。|
|17-02|Platform/Application/Drivers/Integrations源码职责及工程/命名/装配/工具一致，兼容例外明确；核心不依赖WPF/具体协议。|
|17-03|三条完整参考链、所属实际WPF页面/VM/布局/导航/维护；选择构建实际闭包，一个实例服务多窗。Supply/Inspection保留。|
|17-04|配置单一权威、CAS/失败保旧/受控生效；平台数据库机制与业务迁移/仓储/事务分离。|
|17-05|读码/称重/PLC/WCS声明→工厂→配置→签名独立扩展安装→绑定→调用→诊断/重连/停止；不放宽SDK/原生门禁。|
|17-06|重量参与/来源/单位/精度/缺值及实测关联；WCS成功PLC未知重启、防重/迟到/配置变化原身份。|
|17-07|受影响构建、打包、解压启动停止及配置/旧数据/原包兼容必要风险验证，首次失败保留。Windows实际客户端NOT_RUN，真实设备/协议缺资料BLOCKED。|
|17-08|有效指南/样例/manifest/逐项证据/源SHA/产物hash/运行方式；开发分支提交并main PR，不擅自合并。|

实施顺序：职责迁移及兼容入口；配置/存储机制；阶段契约与独立业务；参考设备/集成及扩展；WPF装配；定向风险验证；冻结源码、发布候选、解压冒烟、完成矩阵及PR。每项独立提交并核查依赖。仅按影响运行既有baseline/architecture/operation-closure/conformance/closure，不为数量增加无关全量测试。

技术依据（2026-10-18核验）：[Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)、[SQLite在线备份](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)、[WPF绑定](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/)。这些机制不证明现场协议/设备资格。
