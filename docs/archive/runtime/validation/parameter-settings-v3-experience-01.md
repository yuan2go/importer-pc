# PARAM-UX-02：参数 V3 独立商业体验复核

> 后续合并前审查与角色目录补修见[PR81审查记录](pr81-role-label-review-01.md)；下文为原体验阶段的源码及验收快照，保留其实际身份。

2026-10-21。结论：**经本轮针对性完善，常用桌面键鼠路径有条件通过商业体验复核；尚不能宣布整套产品或目标工控机验收完成。** 在1366×768及1092×614 DIP，单一对象目录、中文标题/字段层级、运行状态、输入反馈与固定保存栏已经具备清楚且可持续操作的桌面品质。800×420可滚动、可定位和保存，仍是兼容下限，不是舒适工作尺寸。结论来自设计、实际代码、原生截图和键鼠任务的交叉核对，断言数量只说明相应工程行为。

## 基线与独立性

- 开始及收口核对 main 为 `767075abe62a77c30e70766bb5d3f1feca02686b`。PR80由开始时 `8355813` 推进至 `d245b12eb5683c3dfd93bb6a847d8ca90dd01de2`，本分支已继承其全部最新证据；后者仍为开放 PR。
- 用户提及的 [CI 39546557135](https://github.com/yuan2go/importer-pc/actions/runs/39546557135) 已完成成功，PR80最终 HEAD 的 [CI 39549856555](https://github.com/yuan2go/importer-pc/actions/runs/39549856555) 亦已成功。它们及冻结介质 `bfec0a5672d846a865db62af9ef6a573a953f914` 仍只证明各自身份，不能替本轮修改背书。
- 在独立工作树、分支 `codex/parameter-v3-experience-audit` 操作，没有修改正在使用的 PR80 工作目录、推送其分支、取消或重启它的 CI。本轮 main PR 依赖 PR80，增量审查应相对 `d245b12`；先合入依赖再按保护规则整合。工作流按 PR 号隔离并发。
- 依据 [PARAM-UX-02](../../design/parameter-settings-experience-02.md)、[V3 27张正式图与原型](../../ui/parameter-settings-v3-20261020/README.md)、HANDOFF、49条迁移记录及现有配置合同。27张图做了完整结构对照，关键状态查看原图；未将图集概览称为逐像素验收。原型源代码用于核对流程，原生运行才作为本次软件证据。
- 使用项目 Impeccable Operate / WPF Assessment A 和 UI/UX Pro Max `--stack wpf`。原生平台不使用网页检测/CDP评分。已有明确产品基线及修正授权，未再进行产品访谈或另建框架。

## 可复现差距与本轮修正

| 问题 | 修改前如何复现及影响 | 本轮结果 |
|---|---|---|
| RCS层级扁平（P2） | 打开完整RCS字段；服务地址、协议、心跳、请求编号连续堆叠，高频连接与低频编号责任没有视觉区隔。 | 服务地址独占宽行；“协议与心跳”独立分组；请求编号收在44 DIP标题的原生展开组，搜索/错误定位仍自动展开并聚焦。 |
| 运行证据像技术日志（P2） | 打开相机/RCS；版本、指纹、状态与地址处于同一灰色摘要，难以快速区别状态和草稿。 | 第一行用语义色加文字显示状态，地址可复制；版本/指纹进既有“技术信息”。失效显示未知，RCS本地就绪明确不代表远端接受；不新增探测接口。 |
| 固定区域挤压表单（P2） | 参数上方常驻工具行及正常读取技术反馈，占用小窗口高度。 | “设置工具”进入统一底栏，技术任务打开时才显示返回任务头；隐藏精确匹配的正常读取消息，保留异常和安全提示。125%同尺寸原生日志中800×420编辑高度由PR80的166.4增至227.2 DIP，保存仍44 DIP。 |
| 系统/备份长输入拥挤（P2） | 站码、长备份路径与普通数字挤在同一表单行。 | 站码及路径单独宽行，间隔/份数成组；不改变值、归属或保存权威。 |
| 无效相机IPv4缺少本地反馈（P1） | 公开海康schema输入 `192.0.2.999`；原生本地校验没有定位错误，修改前专项明确FAIL。 | 只对已安装海康实现复用现有驱动规范地址规则，中文字段提示，原文/选区保留，审阅前阻断；缩写、前导零、IPv6、全零/广播亦覆盖。Application/驱动仍是原权威。 |
| 日常目录泄漏内部名称（P2） | 通过真实日常配置加载完整候选，维护组件显示 `login · maintenance`；首配夹具不会暴露此差异。 | 从现有角色选择只读投影“维护认证”等名称；不复制或改写绑定，同一实例兼任角色时合并显示。未知未绑定实例继续保留可区分身份。 |

没有把宽屏“两列表单”误判为缺失：最初孤立控件截图与完整窗口布局不同；补充完整窗口后确认原布局机制可用，未重写布局面板。没有为了外观更改取格口/心跳编码归属、四类参数、完整候选、CAS、维护授权、暂停、未知原提交核对或运行/待重启责任。

前后关键原生图：

| 对照 | 修改前 | 修改后 |
|---|---|---|
| RCS层级 | [原生RCS](../evidence/parameter-settings-v3-experience-01/before/rcs-1366.png) | [分组与运行状态](../evidence/parameter-settings-v3-experience-01/after/rcs-1366.png) |
| 相机主表单 | [原生相机](../evidence/parameter-settings-v3-experience-01/before/camera-1366.png) | [连接与编码](../evidence/parameter-settings-v3-experience-01/after/camera-1366.png) |
| 小窗口 | [800×420](../evidence/parameter-settings-v3-experience-01/before/camera-800.png) | [字段定位后](../evidence/parameter-settings-v3-experience-01/after/camera-800-focused.png) |
| 错误与修复 | [原校验失败日志](../evidence/parameter-settings-v3-experience-01/before/WINDOWS-WPF.log) | [原文与中文错误](../evidence/parameter-settings-v3-experience-01/after/field-error.png) |
| 长输入与低频项 | 原生表单/源码核对 | [备份](../evidence/parameter-settings-v3-experience-01/after/backup.png)、[请求编号](../evidence/parameter-settings-v3-experience-01/after/request-range.png) |
| 日常组件名称 | `StructuredConfiguration.cs`以Kind构建日常实例，而原目录按Role生成名称；源码可复现 `login · maintenance`。 | 最终专项按实际角色绑定核验名称；配置对象未替换。 |

## 设计还原和操作体验判断

| 范围 / 正式图 | 实际判断与证据边界 |
|---|---|
| 相机/系统/RCS/备份/高级项：01、02、04、09、14 | 常用路径达到。真实WPF为清楚的对象标题、运行摘要、组标题、字段与说明五层；正常文字、中文断行、间距和焦点可辨。相机长地址、备份路径已给予宽行；无需堆叠卡片或重复导航。 |
| 请求/筛选/数据/处理/显示：03、15、18、19、20 | 保留V3单目录及原字段映射，参数/首配共模。完整V3回归覆盖原有跳转、筛选和归属；没有把所有长文本/枚举组合都宣称视觉验收。 |
| 错误/搜索/关闭：05、11、12 | 本轮实际完整字段验证定位、保留原文、目录往返、刷新保选区；键鼠使用Ctrl+F、Enter、Ctrl+A、Tab及工具往返。完整V3回归继续覆盖退出及草稿责任。中文文本插入不等于完整IME组合。 |
| 审阅/待生效/冲突：06、07、22 | 原生差异审阅、无变更阻断、Pending/Effective及冲突路径保留并回归。宽屏层级可读，敏感值保护不因视觉重排退化。没有将“保存”解释成生效或远端接受。 |
| 连接检查/驱动不可用：08、21 | 实际入口进入既有对象诊断，真实运行摘要依据快照；失效变未知。没有实现原型里的假探测成功。真实设备断线、缺SDK、延迟与重连的操作感受待联调。 |
| 安全模板：10 | 沿用已有入口和合同；本轮未另做模板采用实操，不增加导入平台。此图不记新的端到端PASS。 |
| 作业：16、17 | 三组作业信息与业务实例不变；受影响宿主包含在原生回归。真实RCS故障对现场节拍及实体处理的影响不在合成证据内。 |
| 保存未知/进行中/未保存：13、23、24 | 查阅既有守卫和结果分支，原生回归保留审阅/结果相关断言。本轮未新注入磁盘故障或真实网络延迟来评定这三态的全程反馈体验，不能宣布全部异常交互验收完成。 |
| 尺寸：25、26、27 | 实际125% DPI，1366×768、1092×614、800×420 DIP原生截图。前两者可用于日常操作；最小窗口需滚动且主标题可能随内容滚出，当前对象仍由目录选中项辨认。定位后完整输入在固定底栏之上。没有把逻辑窗口缩放等同于三个实体工控机。 |

控件实测：地址输入约38.4 DIP高，底栏主操作44 DIP高，展开标题最小44 DIP；符合本轮键鼠使用判断，**未据此认定触摸可用**。正常状态有颜色与文字双编码；不以颜色单独传递结果。当前视觉比原有密集表单明显改善，仍采用平台原生控件，未承诺与HTML逐像素一致。

按Impeccable 0–4“质量”尺度（4为优秀，不是严重度）记录原生Assessment A：状态可见性3、业务语言3、控制与退出3、一致性3、错误预防3、识别优先3、效率3、简洁美学3、错误恢复3、帮助说明2。帮助仍偏规则文本，最小窗口滚动成本、触摸和完整异常延迟反馈限制了更高评分；这些分数是本轮审查判断，不是用户研究统计。四类目录提供分组识别；编号、实现版本等低频决策渐进展开，常用路径无需一次面对全部技术选项。

## 原生验证及身份

最终产品源码为 `e5444920f48b05443286a4262e767d95c9fb449f`；修正提交为 `07d0817`、`95ad990`、`e544492`，其间仅继承PR80证据并恢复Windows稀疏工作树漏写的两份历史文档原blob。完整回执、DLL哈希、计数见 [RESULT.json](../evidence/parameter-settings-v3-experience-01/RESULT.json)。所有新图为真实WPF窗口渲染/Windows抓图，未用HTML替代或重绘；设备、地址、身份和配置为获授权公开schema及合成数据。

实现集中在 `src/Ipc.Application.ParcelFlow.Desktop/`：`ParameterSettingsControl.Capabilities.cs` / `Policy.cs` 组织字段，`Runtime.cs` 与 `ParameterRuntimePresentation.cs` 分层呈现既有运行证据；`ParameterEditorModel.cs` 提供本地格式反馈和角色名称投影，`StructuredConfiguration.cs` 传递原角色引用。`MaintenancePage.xaml`、`SupplyStationViewModel.cs`、`ParameterSettingsTheme.xaml` 调整工具位置、正常反馈及原生展开标题。定向夹具为 `tests/Ipc.Desktop.UiVerification/ParameterExperience.cs`，入口为原验证程序 `Program.cs`；无新包、数据库、部署参数或后台服务。

- 修改前：`8355813` 产品源码加本轮检查夹具；6张完整窗口原生图，13项通过后IPv4检查FAIL。早期夹具直接使用首配候选，正常布局有效，但不能证明日常脏状态与保存栏联动。
- 修正夹具后改用日常ViewModel的同一个ParameterEditor及保存状态，最终完整字段专项33项通过、13张原生图。覆盖三尺寸、固定保存、输入尺寸/定位、无效原文/选区、驱动格式边界、低频展开、导航对象稳定性、宽路径及失效摘要。合成完整schema候选不提交到生产配置。
- [人工输入回执](../evidence/parameter-settings-v3-experience-01/manual/INPUT.json) 固定为 `95ad990`，仅证明该阶段搜索、中文插入、Tab与工具往返留稿；其后的目录命名改动由最终源码专项验证，未挪用人工证据身份。
- 最终V3完整原生回归142项通过，保存审阅、导航、敏感字段、错误更新、运行摘要与两宿主关键路径继续成立。新专项与原回归不合并宣传为“175项美学通过”。两个Release构建均0警告/0错误。
- 保留中间失败 [layout-flush-failed.log](../evidence/parameter-settings-v3-experience-01/development/layout-flush-failed.log)：夹具切页后立即查询视觉树，未等待布局；改为UpdateLayout后原断言通过，没有删除断言、跳过关键检查或放宽成功条件。
- 未重打安装器/ZIP；PR80的1.7.0介质不包含本轮改动。本轮是源码及本机定向原生验证，后续发布须为新身份构建并检查启动。不新增生产默认或接口支持声明。
- 清单按暂存Git blob核对本轮变更的SHA-256、字节数及blob身份；manifest自身哈希、报告相对链接和全部新增PNG完整性核对通过。V3原件54文件树与PR80一致，其中FILES.json登记的53项哈希逐项一致。本轮未重跑全部历史归档检查，不将此前298项历史问题的比较结果改写为新全量PASS。

复现（Windows，仓库固定.NET SDK，输出使用任务自己的TEMP目录；先按现有文档恢复依赖）：

```powershell
dotnet build tests/Ipc.Desktop.UiVerification -c Release --no-restore -m:1 -nr:false
dotnet build src/Ipc.Desktop -c Release --no-restore -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -m:1 -nr:false
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-experience
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-v3
```

`--parameter-experience --interactive`可留在自有合成窗口走查，关闭结束；临时运行数据不进入公开仓库。公开范围仅源码、合成截图/日志/哈希，不包含SDK、字体二进制、凭据或客户原稿。原设计54文件、历史REQ/BR/CAPI/D/RV及验收身份不改写。

## 尚未完成的验收

1. 目标工控机实际分辨率、100%/125%/150% DPI、字体渲染、亮度与触摸；特别是约38 DIP输入、菜单、滚动条的手指操作。800×420仅兼容，不建议作为舒适主窗口。
2. 完整中文IME候选/组合、长真实站码/路径/错误信息、屏幕键盘遮挡、辅助技术及长时间使用疲劳。
3. 真实SDK/RCS、授权身份、连接失败及恢复、保存延迟/Unknown/NotSaved全程体验，现场真实实体与数量合同；既有Production能力不足时继续拒绝，不以本轮合成就绪替代。
4. 包含本轮修改的新发布介质启动、完整安装/升级/重启、双账号与现场连续运行。PR80已有介质结果保留原身份，本轮没有重做或扩张。

以上是明确待验范围；已确认的本轮软件界面差距已实施针对性修正，未另建配置平台，也未宣布所有历史RV关闭。
