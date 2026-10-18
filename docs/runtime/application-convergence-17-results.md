# Application17 完成矩阵与实际交付

执行基线 main `6e9c890a7483c3006b3448c203570ef2ca19ed25`，PR16已合并；开工及交付前均无并行开放PR。原工作区未跟踪资料未改动，独立工作树/开发分支`codex/application-convergence-17`。前置设计/兼容映射和原字节整理提交 **54abf1f** 先于代码；主体 **b86f21a**，Windows机级政策补齐 **2dd3c04**，确定性发布路径 **f184a6b**。

当前工程实现及完整显式参考链已交付，必要验证PASS；**Windows实际客户端NOT_RUN，真实读码/秤/PLC/WCS及现场BLOCKED，Production NotReady**。未取得既有现场C#工程/SDK/协议授权资料；本包继承main上的F-02参考工程，不宣称旧现场迁移。原REQ/BR/CAPI/D/RV、SQL1～5、旧请求及签名包不改写。

源与候选冻结：**f184a6bb9aeea36f236313457a7c58936c1768c4**。当前[运行/配置/扩展/恢复指南](application-convergence-17.md)、[checks](application-convergence-17-checks.json)、[输入指纹](evidence/application-convergence-17/source-freeze.json)、[完整证据](evidence/application-convergence-17/README.md)。交付后文档提交只补报告/证据/索引，不改变冻结运行代码、候选或既有不可变包。

|AC|具体交付与验证|边界|
|---|---|---|
|17-01|最新main、工作区/PR核验；AGENTS/INDEX/框架决策/扩展/16交付/D/RV/公开范围及v1.1 HTML/CSS/JS/交接；设计/兼容映射与前置原字节单独提交。交付时补发现README16当前入口，另保留原字节并更新。|历史资料/批准不倒写；客户原件/私密资料未进入新交付。|
|17-02|Application单数源码归类，Supply/Contracts/Inspection迁移；原Adapters拆为Drivers与Integrations、旧公共类型TypeForwardedTo；Supply所属UI按选择链接进宿主。构建/选装/脚本/装配清单/源校验器同步。|旧程序集/命名空间/StationConfig是明示兼容例外；公共字节变化不被主版本相同掩盖。|
|17-03|Sportmaster相机→WCS格口、DirectPlc相机→重量→PLC、WcsToPlc相机→重量→WCS→PLC三链；各自真实Workstation.xaml/VM与编译选装。共享窗口/历史/维护/辅助窗依应用契约，一个实例。三个WPF选择及默认Supply交叉构建；三个portable解压实际链/重启/停止。|DirectPlc候选不含Ipc.Integrations.dll；Supply/Inspection原行为与原恢复/批次回归PASS。Windows交互/双窗/DPI仍NOT_RUN。|
|17-04|Platform.ConfigurationRevisions提供唯一Effective/Pending、CAS/版本/校验/受控生效；Application拥有字段/依赖/屏障。Platform.Storage提供连接/校验和迁移执行/online backup；各Application拥有SQL/仓储/事务/原件恢复。Supply连接与备份复用机制，原迁移字节保持。|非法配置不改旧权威；启动构造失败回退原Effective并保留失败候选，下次启动不自动重试失败候选。生产权限缺口未用参考维护替代。|
|17-05|沿用签名/永久身份/精确依赖/ALC/使用租约/consumer登记，新增严格weight/plc工厂类型；外部Git源码实际独立构建两次内容一致，原观察1.0.5及新能力1.0.0安装、绑定、配置、激活、健康/实际调用/停止。Windows仍走受保护ProgramData、指定账户/机级租约及受保护提升宿主。|未放宽旧观察ABI例外/PInvoke/原生加载。真实SDK/驱动prerequisite无证据仍拒绝，Windows ACL/UAC实际NOT_RUN。|
|17-06|Disabled/Configured/Measured、单位/decimal精度/缺值/质量/稳定性/时效/测量ID与同Admission关联；两个阶段冻结原包/实现hash/配置/目标/协议/报文。WCS T3与PLC意图原子关联；PLC丢回执后重启原查询，双方尝试各1，独立peer执行1。|传输/协议确认/业务接受/物理完成分别保存。无记录不授权重发；未知/冲突/缺原件保持隔离，旧请求不使用新目标。|
|17-07|三链/错误重量/同码不同实体/重复回调、T3原件恢复、停止超时与租约/背压、配置构造失败保旧、实际backup全恢复集合/隔离启动及原peer位置、外库/改SQL拒绝。旧六个受影响scope、15工具、10归档工具PASS；六ZIP完整性与公共合同同字节。|首次失败和首轮未采用候选保留；不运行无关默认全量，不以测试数量证明现场通过。|
|17-08|源码开发分支提交、当前指南/配置/接入/矩阵/manifest、六个候选及独立源/签名包公开预发布；main PR由实际链接登记。|不合并main、不强推、不改仓库可见性。Windows/真实设备按[后验](validation/application-convergence-17-windows.md)集中处理。|

## 实际验证和产物

SDK10.0.402/runtime10.0.13，macOS26.5 arm64；locked restore、完整solution以及三个选定WPF工程均0错误/警告。最终f184源码定向执行baseline、architecture、operation-closure、conformance、closure、maintainability，全部具有完整scope尾标；新Application风险执行完整尾标。没有关闭旧RV或声称真实接口验证。

新测量/PLC恢复/配置与资源测试是应用服务+SQLite+持久参考接收账的实际运行，不是单独接口返回成功。外部包旅程使用独立Git源`d7a53b39a1b996d25b98cde09949e77e179f6403`；冻结manifest `E8AC24D0142073E3EC254C36A17A1220F65F00B2BD52D382F64AC61C13E2BE76`，实际reader/weight/WCS/PLC工厂均由已安装ALC加载。重复构建原manifest/payload一致，PSS签名字节不同可预期；私钥未交付。

[预发布候选与附件](https://github.com/yuan2go/importer-pc/releases/tag/application17-reference-f184a6b)，仅供全新隔离合成根，独立TEST_ONLY信任。六包均验证全ZIP CRC/精确文件集合/每文件SHA。Windows候选含真正所选WPF及自包含依赖，portable只含所选核心。全部六包公共Ipc.Platform/Ipc.Application.Contracts精确字节一致，加载门禁未放宽。

|候选|SHA256|
|---|---|
|sportmaster-portable.zip|`f3735a0a7bbf01b54839f95c694262d8df3b231e18c433b3cc09fc071c195be5`|
|sportmaster-win-x64.zip|`b2e698573130585bc420e20505227764bd18fabb70a51d3da890606f78666d81`|
|direct-plc-portable.zip|`386dfa7a334e22693aaef8d1fcfbc435389e7c9acf90d4a8e58ae415d759fea0`|
|direct-plc-win-x64.zip|`ef788ea13798ddf65d4eb4aa82ff100ba8e869bdf33f6b953580646b706427cd`|
|wcs-to-plc-portable.zip|`d2c12c5361b2a568273f92db59fa8ec0829cd8dc4a6d7647ac10ffcd7f6ab9ce`|
|wcs-to-plc-win-x64.zip|`ec272cf5064a4113f5864ef6520d75863da632bb42e08a531dcd7318d8ba35c8`|

包内APPLICATIONS.md提供真实运行方式；Windows入口为desktop/Ipc.Desktop.exe，portable为`dotnet app/Ipc.Assembly.dll --config app/config --data-root <全新隔离根>`。控制台/WPF均显式标参考环境。程序不含真实厂商PLC地址、握手、SDK方法、WCS生产合同或未经授权的设备控制。

首轮编译/工具/冻结宿主失败见[原始失败公开副本](evidence/application-convergence-17/log-custody.json)；未删除断言或把停止超时变成成功。原目录路径是唯一公开替换项，原hash可对账。全仓资产校验仍可能报告历史源图/字体/外链问题，本包新文件/链接/清单错误另行修复，不伪称已修复原设计资产。

全仓资料校验实际结果为 **FAIL（既有源资产）**：265个原PPTX媒体类型不符、2个原UI元数据标记保持原件；本包新增清单/内容身份/链接错误 **0**。迁移引起的历史源码链接已验证原Git blob并固定59个链接到各报告原分析SHA，原报告修改前字节归档，结论/行号/当时验收状态未改。未通过放宽资料校验消除历史失败。远端预发布9个附件已核对大小及GitHub提供的SHA256，见[远端回读](evidence/application-convergence-17/remote-release.json)。
