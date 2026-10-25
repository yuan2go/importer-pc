> 2026-10-26批量部署整改：最新main a8cd4314已整合；冻结源183f9fac的core/14项与实际只读CLI通过，CI40216393477工程/原生/打包通过但归档失败，共同D盘归档根已修正，完整CI待新运行核对。旧run40210140349的FAIL和0a3d4152原PASS均保留原范围，上一轮完成结论不能作为本次验收。当前状态见[续报](lifecycle-batch-preflight-01.md)。

当前批量整改、原CI失败/复现、新源码和冻结验证均见 [2026-10-26续报](lifecycle-batch-preflight-01.md)。以下保留上一轮原记录，不将其中“最终/完成”扩张为本次批量部署结论。

# 生命周期优化 · 验证与交付边界

开工 main `3a5b1d13`，规划原件提交 `4c42f567`。本批测试使用 D 盘独立合成数据，真实 Application、SQLite、Windows WPF/HWND/Dispatcher；设备、身份预检和外部服务按夹具明确模拟。**最终冻结源`0a3d41526babeb290f9c4ddfbfcdab4a977b8bc8`，2026-10-26收口；当前主PR为[#108](https://github.com/yuan2go/importer-pc/pull/108)，未合并。最终结果见末节及frozen-0a3d4152证据；中间候选不作最终资格。**

工程提交`8488b1ce`；启动说明同步提交`36fb420e`；只读备份绑定修正`9fa0e80f`。实际候选的SourceCommit以其ASSEMBLY.json/BUILD-INFO.json为准，后续报告提交另列。最终证据见下表及[evidence清单](../evidence/lifecycle-ux-optimization-01/FILES.json)。最终报告提交与冻结产物分别计证。

## 必要验证

|范围|入口|结果|
|---|---|---|
|原操作、完整历史、日志资源、备份和到期维护|`dotnet run --project tests/Ipc.Application.Verification -- . --lifecycle-ux`|PASS；153原实体/四页、实际CSV/取消、日志句柄、备份、维护成功/中断/丢终态|
|原生历史、权限到期/返回、停止失败和生效读回|`dotnet run --project tests/Ipc.Desktop.UiVerification -- . --lifecycle-ux`|PASS；实际权限到期解释/原页返回、失败停止、真实B读回暂停、原请求A；卸载三状态原生呈现|
|安装原状态/未知回执、首配键盘和撤权|`--installer-first-run-ui`|PASS（具体日志见evidence清单）|
|日志分页/上下文/取消/两种导出及到期|Application `--management-mode`、UI `--reliability-logs`|PASS（具体日志见evidence清单）|
|PR107/首配共模|UI `--parameter-guidance-startup`|PASS（具体日志见evidence清单）|
|固定清单/占用/保留额外文件|Verification `--commercial-removal`；Application `--installer-change-guards`|PASS（具体日志见evidence清单）|
|共享契约|Sportmaster、DirectPlc、WcsToPlc 装配构建|PASS（具体日志见evidence清单）|
|当前指南、依赖库存和启动|release_documents 定向测试、同源候选及启动冒烟|PASS：当前指南/库存及冻结发布DLL原生；普通CLI成功启动BLOCKED_ENVIRONMENT，见下文|

## 明确保留的门槛

G01：RCS 原合同没有权威结果 Query/获批准人工裁决、去重和实体责任政策。可读交接不清偿 Unknown，也不解锁重发。G02：没有获批准的管理密码恢复政策，保留决策入口；没有默认密码、删账重新首设或 Windows 权限后门。二者继续 `BLOCKED_EXTERNAL`。

本批不控制设备，不实际安装/卸载当前机器软件，不执行真实 UAC、系统登记往返、重启删除、SDK/RCS、目标工控机触屏、24h/72h或现场验收；这些为 `NOT_RUN`/外部待验。未改历史删除算法、SQL/索引或完整维护预算，不重复大库资格测试。旧 D/RV、FAIL/PASS、介质及 manifest 保持原身份。

## 本轮发现与修正

失败原件与最终PASS分别保留：首次到期维护回执目录不符合原恢复集允许清单，改沿原32位身份目录；丢终态核对持有自身SQLite会话导致WAL读取失败，改在核对原指纹前释放；G02政策长文挤出首次设密视口，改为既有密码部署的可展开政策；新增日志日期断言起初期待不存在的提示文字，改为实际“尚无有效查询快照”，双导出禁用及真实日期/时区断言完整保留。未删断言、跳过关键测试、吞异常或放宽业务守卫。

原生DPI实测1.25，标准1366×768、最小800×420客户区；实际HWND/Dispatcher、焦点、键盘及滚动与操作分别计证，不代表改变OS DPI矩阵或触屏实测。日志上下文401条原记录约388万字符，呈现65536字符并保留完整导出说明；新增原生日期/快捷范围/UTC偏移绑定与条件变化后的两类导出禁用验证通过。对应后端管理模式测试覆盖真实来源、上下文、固定选择、完整/时间范围导出、撤权、取消和原子文件行为。

本批只执行受影响定向验证及三产品装配构建、同源候选/启动冒烟；不会因旧测试列表存在而全量重跑大库和长期测试。源码冻结和候选之后无新源变化时不重复已通过的全量套件。

## 前轮候选与启动（36fb420e，已被备份绑定修正替代）

|对象|身份/结果|
|---|---|
|前置清理/原规划提交|`4c42f567`，业务编码前保存原件/过期指引处置|
|独立工程提交|`8488b1ce005439ba5473685ec09125b6b86779e9`|
|前轮冻结源|`36fb420e8ca9b0f85bb6020418298ceb22328f39`，仅同步生成启动说明/增加随包引用断言；未改前述已验证业务代码|
|portable ZIP|18756068 bytes；SHA256 `e987560c5aa8958c1f000dbdfae4e8210cfffa31c0e203fe8a19675287ff93e0`；110库存文件|
|Windows x64 ZIP|134213218 bytes；SHA256 `fbf1a6b965f1e49931cc702bed15cb6811adf8d6c84fd94150c6809abc54f127`；741库存文件|
|版本|SM1.13.0 / Contracts1.19.0 / DB9；DirectPlc及WcsToPlc1.7.0仅受影响装配构建，不声称制作了其新介质|
|库存/当前指南|两个目标所有SHA256逐一通过；当前入口存在；生成启动说明不再引用已移出的旧UI手册；编译合同/组合/依赖一致|
|实际Windows EXE入口|`Ipc.Desktop.exe --environment-report` exit0，.NET10.0.13、Win11 x64；只读环境入口，不创建业务实例|
|冻结发布DLL原生日志|`verify-commercial-wpf.ps1 -ReliabilityLogs` PASS；SourceCommit=FixtureSourceCommit，二进制散列在RESULT；真实HWND/Dispatcher/SQLite，明确合成身份/端口|
|冻结发布DLL生命周期|复用上述同源NativeMedia工程，`--lifecycle-ux` PASS；原生配置B读回/暂停/原请求A、停止失败/重开、到期同页恢复及卸载三状态；最终截图另存，未覆盖前一源图|
|普通CLI业务启动|**未通过/环境受阻**。portable collector 20秒未取得Started、exit1、空stderr；原FAIL和事件保留。随后只读RequireRuntimeIdentity返回缺维护安装的machine runtime policy及指定账户。没有为测试继续安装/改策略；不把夹具启动当作正常机器资格通过|

产物仅在本机`D:/deliveries/lifecycle-ux-20261025-36fb420e/`，未上传ZIP/安装器，不是公开发行或现场交付。`8488b1ce`先前portable仅保留其原源身份，不混入最终候选。最终文档与PR头可后续提交，不能倒写两个候选的SourceCommit/manifest。

前轮实际命令先加载`. ./scripts/project-environment.ps1`：

```powershell
python scripts/package-application.py --application sportmaster --target portable --output D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-portable
python scripts/package-application.py --application sportmaster --target win-x64 --output D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64
./scripts/verify-commercial-wpf.ps1 -Media D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64 -Output D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e -ReliabilityLogs
$env:IPC_FROZEN_DESKTOP='D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64/desktop/Ipc.Desktop.dll'
dotnet D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e/workspace --lifecycle-ux
```

普通CLI collector失败命令及配置原件记录在失败JSON/事件中；本机策略不足时不再次调用可能进入首次运行维护的普通入口。只读策略诊断是观察结果，不证明collector失败的更细分进程原因。正常非提权业务启动、系统往返和目标资格须具备获授权安装环境后单独完成。其他独立工程已完成，G01/G02继续BLOCKED_EXTERNAL。

应用层定向/管理/安装守卫、原生首配/参数/日志、三产品构建、三个指南测试、17兼容测试、5商业交付测试与1发布构建测试均通过；全部保留原命令/日志。没有全量、大库或长期重跑。原始证据及失败均有单独哈希，不改写旧介质和历史D/RV。

## 只读备份状态绑定增量

`488c9561`补充既有原生稳定性夹具的明确根退出影响审阅，没有跳过停止/资源断言。该旅程实际进入运行上下文的自动备份页时，发现新状态TextBox默认TwoWay绑定只读计算属性BackupStatusText，触发WPF异常。36fb420e的原两项PASS覆盖不到此入口，保持其原范围；该候选被此已知缺陷替代，不用于最终交付。

`9fa0e80f`将正式状态投影明确设为OneWay；增强同一真实恢复→参数→备份→退出旅程，读取实际正式备份状态，保留原责任/七天设置/Dispatcher卡顿与释放断言。草稿恢复改为恢复实际原值，兼容已有显式默认。修正前失败原件和修正后定向证据分别保留。最终候选重新从含此修正的冻结源构建，manifest仍保留原身份；不再次尝试已知策略不足的普通CLI入口。

修正后本机`--win11-stability`通过原责任恢复、七天编辑、正式备份状态读取、独立Dispatcher卡顿/恢复、根退出影响审阅、异步关闭和计时器注销；DirectPlc/WcsToPlc公共控件构建零警告/错误。此为短程原生回归，不是长期稳定性资格。当前main PR为[#108](https://github.com/yuan2go/importer-pc/pull/108)，补充验证期间保持draft，最终同源候选验证后恢复待审；不合并。

## 必要整合回归与布局收口

首轮PR头ed925459：Linux aqa01-boundaries成功，Windows frozen-sportmaster失败，原CI/产物保留。卸载终态、历史按钮及G02密码窗口三处旧控件断言按实际新任务语义更新，仍检查禁止重复修复/认证、每种取消只在原任务运行时可见、密码修改/撤权；没有删断言。参数搜索失败与备份只读绑定的半途重建相符，修正后冻结48e6da29的参数V3全旅程通过。

随后真实最小窗口暴露历史范围行高度/紧凑详情标题未计入预算，以及经典壳新增44DIP模式入口后不足200DIP作业区。6f8c43c1监听范围父容器与详情尺寸并计入占位；a89bd8eb调整紧凑外边距，保留字体和200DIP作业区，并将经典暂停/恢复/页面菜单保持44DIP。原断言保持，增强实际命中尺寸检查；本机完整visual和task-hierarchy/真实请求链通过。f5d7016a/d3717e02的投影夹具公开完整历史契约，避免把快照fallback误计为固定查询；日志断言明确已应用范围和时区。

affdd79c仅澄清日志日期清空后仍须选择固定起止，历史可选时间边界保持。48e6da29是中间候选，已被上述布局修正替代；其局部PASS和新增FAIL分开保存，不用于最终交付声明。最终发布源/库存以候选ASSEMBLY/BUILD-INFO/SHA256SUMS为准，最终冻结验证在报告末尾追加。

本机已通过新增最小窗口/完整查询视觉、经典与默认全部页面/真实请求链、参数壳/草稿/会话、冻结V3参数、启动体验、模式/改密码及短程恢复→备份→退出旅程。未修改删除算法、索引或后端业务守卫；未重复大库/长期资格。两次必要重冻结均有新发现的实际UI缺陷，旧候选及失败原件不覆盖。

## 最终冻结验证 · 2026-10-26

实现、配置、打包和完整夹具固定源均为`0a3d41526babeb290f9c4ddfbfcdab4a977b8bc8`。同源Windows发布DLL的八套旅程全部通过：parameter-v3（含作业重设计、参数体验、重构/V3）、startup-experience、visual-productization、management-mode、win11-stability短程、reliability-logs、lifecycle-ux、task-hierarchy及真实请求链。SourceCommit=FixtureSourceCommit；运行副本逐一匹配发布DLL哈希；全部原生旅程串行，结束后741个介质文件哈希不变。后续提交只记录报告/证据，不改四棵工程树或随包当前指南。

|最终对象|真实身份/结果|
|---|---|
|SM版本|1.13.0；Contracts1.19.0；DB9。DirectPlc/WcsToPlc1.7.0完成公共壳最后增量构建，无新介质资格|
|portable|18755766 bytes；SHA256 `b62c9999165823e2b44a0cb8f788e8f3d879fde7629e7a693652b58f45f4f403`；110文件库存通过|
|Windows x64|134212735 bytes；SHA256 `239648b7a1d0467e006c0bdcee3e025f3be7c6fedac6ab883c2fb25b73e71d99`；741文件库存通过|
|真实可执行入口|自包含`Ipc.Desktop.exe --environment-report`及`Ipc.Assembly.exe --action business-identity` exit0；在原生旅程全部关闭后串行运行；均为只读入口|
|正常机器业务启动|仍**BLOCKED_ENVIRONMENT**：最终只读RequireRuntimeIdentity确认缺维护安装的machine runtime policy/指定运行账户。未重试普通入口，未安装/改策略；原36fb420e普通CLI失败保留|
|权限/原请求|到期隐藏/返回同页、模式独立于操作员、真实B读回暂停/原请求A不改、停止失败/资源释放、原卸载状态；原断言保持|
|最小窗口与日志|完整固定查询契约、展开筛选/晚到暂停回执均保留完整记录行；经典准入动作44DIP/作业区≥200DIP；日志401条上下文有界呈现与撤权/固定日期/两类导出守卫|
|外部资格|G01/G02继续BLOCKED_EXTERNAL；真实UAC/系统安装卸载往返/重启、目标IPC触控、SDK/RCS、24h/72h及现场NOT_RUN|

最终产物在`D:/deliveries/lifecycle-ux-20261025-0a3d4152/`，目录日期保留冻结批次；跨午夜收口不重命名或重写历史文件。未上传ZIP/安装器；原受限四份附件保持未跟踪。没有合并main、强推或改变仓库可见性。必要验证通过后停止扩测。

运行/复核：先`. ./scripts/project-environment.ps1`；本机可只读运行上述environment-report。正常软件使用合法同源部署的非提权入口，缺策略时不通过链接/环境覆盖或重新首配绕过。完整操作见[当前指南](../lifecycle-ux-current-guidance-01.md)。最终冻结复核命令：

```powershell
./scripts/verify-commercial-wpf.ps1 -Media D:/deliveries/lifecycle-ux-20261025-0a3d4152/sportmaster-win-x64 -Output D:/DevCache/importer-pc/lifecycle-final-native-0a3d4152 -ParameterV3
# 同一NativeMedia工程随后按case-results记录的flag串行运行其余七套；输出目录须为新的独立目录。
```

最终原始日志、八个结果、DLL/工程树身份、精选PNG和完整字节清单见[evidence](../evidence/lifecycle-ux-optimization-01/README.md)。首轮ed925459的CI成功/失败只属于该头；PR最新头远端CI状态按GitHub实际检查，不把首轮结果倒写为最新PASS。工程已完成与正常部署/真实联调/外部门槛明确分列。
