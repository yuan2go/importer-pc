> 2026-10-23 参数与首配呈现增量由 [PARAMETER-UI-REFINE-01](../work-packages/WP-IPC-PARAMETER-UI-REFINE-01.md) 替代：单目录、单正文滚动、中文字段错误与紧凑管理菜单。四类归属、原字段与保存/应用责任不变；下文及历史证据继续保留原阶段身份。

> 历史实施记录：下文操作和字段表保留 PR73 时语境。当前增量入口为[参数继承裁决](../design/parameter-inheritance-02.md)与[现有工作包](../work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)；原现场与发行身份不扩张。

> 2026-10-21 当前执行入口：PARAM-UX-02 / WP-IPC-PARAMETER-SETTINGS-02，参数采用 V3，复用已合并 PR78/79。下文“当前、待实施、不测试”均保留原阶段语境；不再定义当前布局或禁止本轮原生验证。

> 最新 V2 目标见[实施裁决](../design/sportmaster-task-redesign-01.md)。本页记录 PR73 原参数范围与原验证；本轮合并上传不测试、不构建，不扩大为 V2 或现场验收。

# PARAM-01 参数正式重构：实现与验证

2026-10-20。参数及必要联动实现完成，受影响构建和下列定向验证已通过；真实设备、RCS 和现场验收仍待执行。本页是现行参数操作入口，原型和旧发行证据保留各自身份。设计边界见 [现行裁决](../design/parameter-refactor-01.md)，完整 UI 余项见 [工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)。

## 来源与并行保护

开工 main `6784d08`，干净工作区，继承 #70 `20bd90f`；其保存前缺少心跳映射校验的审查缺口由本包补齐。之后 main `ba80d0e` 已合并 #70、#69 安装器、#71 本任务早期快照和 #72 发行回执。本分支 `codex/sportmaster-parameter-refactor` 的实现 `5cd89bd`、整合 `bdd5e65` 保留安装器环境报告/一次授权 helper；后续提交补充最终修复与证据。原 #71 发布介质只包含当时实施中的快照，不改标为本次验证版本。新发行冻结身份及启动结果在本页末尾追加。

## 参数与日常操作

“设备与设置”直接显示系统、业务模块、驱动、组件四类，共同编辑同一完整候选。技术操作折叠到高级维护。固定左右 Reader 的用途、位置、数量和稳定身份只读，可以选同一精确驱动包，参数和连接独立；一个逻辑 RCS 配置左右共用。表单取自实际安装版本描述，缺包显示准确原因。系统 StationCode、驱动 bcr-code、原左右取格口编码语义独立，组件继续拥有 quantity、指定格口、心跳周期及所有协议参数。

缺配不阻止打开软件：复用既有首次配置页，与日常页面共用 ParameterEditorModel、ParameterSettingsControl、静态校验和配置权威。不存在缺配专用页面。缺整个 Sportmaster 种子时提供 Application 声明的固定双位 Production 引导草稿；不自动连接设备或运行 Simulation。已有业务数据而原身份丢失时拒绝生成替代身份覆盖原责任，仍可从原页面选择原配置/恢复。

系统必需默认集中于 SystemParameterDefaults：队列64、调用预算1500ms、未清偿作业10000、常驻历史200、写入保留64MiB；新站身份 station-01，StationCode 默认取站身份且可独立修改；计划备份默认关闭，启用时默认60分钟、保留10份、当前用户 ImporterPc/backups/ApplicationId 目录。已有明确值不被替换。业务规则、真实 IP、生产实体关联资格和 RCS 协议编码需要真实配置，不编造生产值。

新首配规则为空，允许保留草稿，但须填写并校验后才能保存为业务可启动候选。当前配置 BarcodeFilter 缺省时显式使用严格单码校验，不静默写全匹配。维护人员填写规则、输入原因并取得维护身份后，检查完整候选再保存；运行内仍要求暂停、排空和 CAS。保存只产生 Pending，受控重启才应用，重连不应用配置。最后启动证据与当前准入健康分别呈现。

失败保留原始输入和错误定位。校验期间输入变化使返回结果过期，不能覆盖新输入。保存前冻结候选、原修订和原因；回执失败/异常后按权威账、规范化候选指纹核对。结果未知时禁止重复提交，使用“核对上次保存结果”；冲突保留输入，明确重新读取/比较后再修改。旧草稿继续采用原加密存储和作用域，用户显示偏好继续按原用户保存。历史无规则配置无损往返，新增可选字段为空时省略；旧请求和旧实体使用原冻结配置，不被新规则重新解释。

## 筛选与准入

Hikrobot 复制 ABI 内全部候选，最多300个，不再只留前两个。入口对数量、单码4096字符和完整性设置边界；越界保留有界证据并按不完整拒绝，不能因截断误判唯一。驱动保留原始解码，Sportmaster Application 执行业务正则：最长2048字符，\A(?:pattern)\z 整串匹配，单次25ms、总计100ms预算。非法/超时/零匹配/多匹配/不完整分别拒绝；唯一有效码才继续既有可信来源、同实体、身份冲突、去重和左右隔离守卫。

原始候选、选中码、结果与规则指纹存入原输入/作业证据。拒绝观察不创建业务请求，不把历史格口显示成本次筛选结果。已有实体使用原冻结规则，原请求报文/合同/目标/编号继续冻结；没有新请求查询能力或自动重发 Unknown。样例测试使用同一有界匹配器，不保存配置、不占设备、不外呼。

RCS 参数和跨类别映射由组件纯静态代码校验，装配调用同一代码；缺 StationCode、缺失/重复 bcr-code、Target 和协议约束在保存前发现。生产 SDK/资格检查留在受控启动与准入。继承 #70 唯一心跳循环，没有新增发送器。

## 工作包完成映射

| 项 | 本包完成范围 | 边界 |
|---|---|---|
| UIR-00 | 清理当前入口、四分类裁决、文档/工作包/提示/索引及安装描述一致性 | 原件、REQ/BR/CAPI/D/RV、历史发行不倒写 |
| UIR-01 | 四分类、固定双实例、真实描述表单、输入与焦点保留、高级维护入口 | 其他导航和全页摘要改造未在本包扩大 |
| UIR-02 | 字段与跨类别静态校验、缺配/缺依赖、可点击错误定位 | 不外呼校验、不抢占连接；实际资格仍待设备 |
| UIR-03 | 同一完整候选/CAS、保存未知核对、异步输入保护、Pending/启动失败/运行未知 | 原权限、审计、草稿与生命周期继承 |
| UIR-04 | 真实条码筛选准入、拒绝原因、选中码/原候选及旧格口保护 | 其他连续作业与诊断重设计保持余项 |
| UIR-05 | 既有首配页和日常设置共用；缺参可打开、必需系统默认 | 无重复安装/配置平台；模板/支持余项保持 |
| UIR-06 | 受影响构建、关键准入/配置/恢复回归和原生验证 | 目标工控机、真实接口与现场未验 |

## 已执行验证

所有数据均为合成。日志与截图位于 [证据目录](evidence/parameter-refactor-01/WINDOWS-WPF.log)，保留最终结果，不借用旧 PASS。

| 验证 | 结果 |
|---|---|
| Application.Verification Release、UiVerification Release、Sportmaster Desktop Release、DirectPlc Desktop Release、WcsToPlc Desktop Release、Hikrobot Probe Release | 全部0警告/0错误 |
| --parameter-refactor | 35项：第三候选、多匹配、非法/超时、不完整、旧规则/原请求、静态RCS校验、首配同源、CAS/未知、异步新输入保护 |
| --ux-commercial-configuration / --ux-commercial-inputs | 62 / 26项 |
| --configuration-confirmation / --parcel-recovery-p2 | 32 / 100项，包含应用失败、运行未知、原请求恢复和确认身份 |
| --version-compatibility / --sportmaster-rcs / --rcs-heartbeat | 56 / 121 / 26项 |
| Hikrobot Probe --self-test | 27项 fake API 检查 +5项策略检查，包含第三原始候选 |
| Python test_application_compatibility.py | 17项 |
| UiVerification --parameter-refactor | 实际 WPF HWND/Dispatcher：首配缺种子可打开、系统默认、四分类、刷新焦点/选择保留、样例第三候选、错误定位、固定双实例独立参数；1366×768、900×640及800×420 DIP 可滚动 |

Windows 主机实际125%缩放；请求1600×900的夹具实际客户区1553.6×881.6 DIP，截图记录真实尺寸，不冒称目标分辨率验收。通过原生实际键盘/鼠标改左IP、改业务规则/样例并点击测试，再切回驱动，左右输入独立保留；没有连接真实设备。截图：[系统](evidence/parameter-refactor-01/parameters-system.png)、[业务](evidence/parameter-refactor-01/parameters-business.png)、[驱动](evidence/parameter-refactor-01/parameters-drivers.png)、[错误](evidence/parameter-refactor-01/parameters-error.png)、[紧凑](evidence/parameter-refactor-01/parameters-compact.png)。

设计原型48 PNG全部查看，交接/58字段/39动作及源码已阅读；原 HTML 浏览器入口超时/受安全限制，未绕过，也不声明原型浏览器动态验收通过。共享参数控件使用真实WPF验证替代实现验收。最初原生构建因仍打开的测试窗口锁DLL失败，正常关闭后最终构建通过；其他Application初次缺还原资产，按既有各自锁文件还原后通过，没有新增锁文件或降低门禁。

既有确认测试遇到短暂健康刷新窗口，保留其严格可用性断言，限定刷新等待后通过；配置测试严格保留CAS/原请求/草稿断言并适配显式 StationCode 默认迁移。测试夹具释放前一运行的设备租约后再开启独立运行，没有绕过租约。没有删除关键断言、吞异常或把未知发送标为成功。

## 复验与部署

本机 .NET 为 D:/tools/dotnet/dotnet.exe（锁定SDK10.0.402）。仓库根目录可用标准 dotnet：

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --parameter-refactor
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --configuration-confirmation
dotnet build src/Ipc.Desktop -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
# 原生验证用同版已构建桌面DLL检查实际既有首配窗口
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet run --no-build -c Release --project tests/Ipc.Desktop.UiVerification -- . --parameter-refactor
python -m unittest discover -s scripts -p test_application_compatibility.py -v
```

实际输出目录遵从项目集中构建属性；若已选择隔离 artifacts，IPC_FROZEN_DESKTOP 应指向该输出，不能混用旧DLL。新公共契约1.12.0、Sportmaster1.5.0、DirectPlc/WcsToPlc1.4.2、RCS1.2.1、Hikrobot1.0.3及相应组合清单已更新；公共DLL精确哈希机制要求重新构建签名包，旧包不覆盖重签，旧请求仍保留原精确依赖。Parcel DB8未变。

## 外部验证与发行边界

真实 SDK/设备帧、触发与实体资格、真实RCS取格口/心跳合同与编号作用域、目标工控机分辨率/DPI/触摸、完整Windows安装升级与现场验收均未执行。没有将本机合成WPF或portable参考链宣称为这些验收。D批准和历史RV保持原证据状态。全库归档已有UI资产字体、引用协议与元数据问题仍需单独治理，本包不增加豁免；本次manifest与引用按Git规范字节独立核验。

## 冻结发布与最终回执

冻结源码 `220d35931a6e2174d35629a65a01a1b61cc39b21`，业务1.5.0。其后仅补充桌面备份数值错误的字段定位及文档/证据，未改变 portable 中的业务、驱动、组件或公共契约；最终桌面与原生定向检查另行构建通过。按既有 package-application 构建 portable 包，SHA256 `0f78368ed20ed2a8f3898eaf353c239a0347997e60b332a6d82e56a91f5e96f6`，见 [发行身份](evidence/parameter-refactor-01/release-identity.json)。这是必要的发布启动验证候选，不覆盖既有安装器介质或宣称完整安装升级通过。其 PARAMETERS.md 为同一冻结提交的操作指南，后续回执提交不改变原包 SourceCommit。

[实际解包冒烟](evidence/parameter-refactor-01/release-smoke.json)通过6类检查：候选文件SHA、实际所选程序集启动、参考相机到最终格口链、重启保持原阶段、有序停止、左右两个读码器。原字段 Windows=NOT_RUN、RealEquipment=BLOCKED 保持；Windows主机执行 portable CLI Simulation 不等于图形或真实设备验收。

两个新包均用独立 TestOnly Simulation 信任构建，经冻结宿主 extension-check 接受且冻结宿主/检查器未改变，见 [静态验签回执](evidence/parameter-refactor-01/extension-static.json)：

| 包 | SHA256 |
|---|---|
| sportmaster.rcs-task 1.2.1 | da02fde4ce44aa2074a75f878823eb70eb421819372ead3d879c6997119c43c3 |
| hikrobot.idmvs-reader 1.0.3 | ac1aeaa2177819cd06dc1673955b7cf73163ebac9ae7534a66e796f3a0b5e319 |

公共 Platform SHA256 `13e95b57c6290ba666abc8fc8d9572363eb9a6aac0c9a772d7ef3c12542dbed3`，Application.Contracts SHA256 `c60b4d9872627ad6dbf4f605b43f86801b03300a8013c46aa6a08458ebcbb609`。原本地测试输出的不同公共DLL被精确哈希守卫正确拒绝；改用隔离测试输出和上述冻结DLL复验，未改守卫或冻结宿主。[签名包实际加载的5项检查](evidence/parameter-refactor-01/rcs-signed.log)全部通过：元数据/无查询能力、精确契约、实际双读码心跳、取格口、停止排空。夹具[绑定身份](evidence/parameter-refactor-01/signed-fixture.json)可复核。未安装到机器登记、未修改信任、未提交私钥或真实SDK；TestOnly 包不能用于Production。

Windows Git 默认保护拒绝历史原型中的 aux.js，首次冻结导出因此失败；仅在发布子进程设置 Git core.protectNTFS=false，使用既有 extended-path 导出器保留原件后重试成功。没有删除/改名原设计资产、覆盖并行工作或把首次失败记录为PASS。历史 Codex 工作包在整合时恢复原 main 字节，最终无该文件业务变更。

验证充分后停止扩大范围。当前参数范围已完成，完整 UI 余项和上述外部验收继续保留，不将其标为完成。

最终清单核对发现 main 的 #69 已删除 InstallerWindow.xaml 及 .xaml.cs，而原 manifest 仍留两项当前条目；本包仅清除这两项过期清单记录，不恢复旧安装窗口。原文件/证据保留在 Git 历史（10c7335）。在该次提交核对的4725项索引原字节的 SHA256、大小及 Git blob 全部一致；这仅证明清单字节身份，不代替全量资产政策检查。


## PR #73 的 Windows CI 入口修复

[首次远端检查](https://github.com/yuan2go/importer-pc/actions/runs/39406264803/job/118572825786)在 `0d4ee6d` 的 checkout 阶段因原型 `source/aux.js` Windows保留名失败，尚未执行构建/测试，不计为应用回归失败或PASS。现有Windows工作流改为非cone sparse checkout，仅不在工作副本落地该文件；Git blob和完整历史均保留。core.protectNTFS=false仅作用于checkout子进程；冻结导出器仅在只读git archive命令设置该选项，仍使用data_filter与extended-path目录，签名包SafePath和部署安全门禁不变。

复用 [统一发行测试](../../scripts/test_unified_release.py) 增加实际Git blob→稀疏工作副本→完整冻结导出检查，验证AUX原字节仍可从Git/导出读取、普通文件完整、原件未删除；其余篡改/混源/ZIP/清理断言保留。Windows本机15项全部通过，现有商业交付拒绝策略5项亦通过，见 [日志](evidence/parameter-refactor-01/release-windows-paths.log)。两次夹具初验暴露主机autocrlf转换，固定合成仓库自身换行策略后原字节断言通过，没有删减断言。远端重跑结果以PR检查为准，不预记PASS。


## ba80 合并快照的源码对照（形成时历史，2026-10-20）

以下保留V2设计轮对ba80的原静态对照。表中“继续完成”是当时判断；PR73已经完成的原参数范围见本文前述证据，V2新增目标仍待实施。


仅静态读取，不新增构建/测试/Windows验证。PR71冻结 `5abe82071471d525686352c094dbb79dc7fea3ce` 已随 PR69/70/72 合入main `ba80d0ea90d86b24f9d9eebd20d075da34320972`；发布范围及原产物见[快照](publication-parameter-snapshot-20261020.md)和[发布回执](publication-pr69-pr71-20261020.md)。

| 已有代码 | 当前可确认范围 | 继续完成 |
|---|---|---|
| `ParameterEditorModel` / `ParameterSettingsControl` | 四分类、真实描述参数、字段/对象错误、定位、基础输入与本地样例。 | 按V2重排唯一编辑归属及对象布局，完善状态/差异/键盘与尺寸体验。 |
| `StartupConfigurationDraft`、启动入口 | 首配与日常共模、缺配可编辑与原责任保护路径。 | 五步独立首配、未完成续办、故障维护返回及正常页面感知。 |
| `BarcodeFiltering` 及准入联动 | 完整候选、受限整串正则、唯一选中、歧义拒绝、旧null兼容与证据保留。 | 原生展示与必要风险验证；已有规则清空不得退回旧兼容。 |
| 桌面/应用 `ConfigurationSubmission` | 规范化候选、配置账及原所有者核对；Unknown/PendingRestart/NotSaved/Conflict。 | 准确呈现结果、保留更新后的输入、冲突/未知的可执行下一步及最终验证。 |
| `RcsConfigurationValidation` 与既有心跳 | 保存前静态联合校验；已有周期、编码、生命周期和HTTP证据。 | 保留两类编码的用途与历史，按V2迁移取格口字段；不把心跳传输当应用确认。 |

三组相机任务展示、waveNo/响应quantity的兼容传递、完整对象维护与V2视觉尚有工作；taskId/skuId原有解析/应用传递继续复用。V2目标见[现行裁决](../design/sportmaster-task-redesign-01.md)，不以临时参数摆放反向覆盖用户字段归属。单窗口安装和已发布原介质继续保留各自SourceCommit，此参数快照及最终合并树未据此取得新安装或现场PASS。
