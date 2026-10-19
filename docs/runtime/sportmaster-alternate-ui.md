> 本分支按[UX修复工作包](../work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)更新现行操作；修改前说明的固定Git原字节见[索引](../archive/pre-ux-commercial-fix-01/README.md)。开发验证、最终媒介身份和Windows/现场验收分别记录，本文不宣告后者通过。

# Sportmaster 第二套原生 UI · 运行指南

用户已批准[设计与字段映射](../superpowers/specs/2026-10-18-sportmaster-alternate-ui-design.md)及[工作包](../work-packages/WP-IPC-SPORTMASTER-ALTERNATE-UI.md)，允许本次衍生 WPF 代码公开，原HTML不上传。主业务继续遵守[架构22](application-architecture-22.md)。本功能继承WCS阶段、实体准入、原请求及驱动会话；UX修复通过既有审计追加最新观察投影，不改SQL1～7。

## Windows 客户端启动

实际 win-x64 候选解包后执行；为参考环境选择全新隔离数据目录，正常运行不同时启动两个宿主争用同一站位数据根。

```powershell
# 默认保持原界面
.\desktop\Ipc.Desktop.exe --ui classic --config .\desktop\config --data-root C:\IpcReference\sportmaster
# 新的双位供包工作台
.\desktop\Ipc.Desktop.exe --ui supply-station --config .\desktop\config --data-root C:\IpcReference\sportmaster
# 新UI的作业记录页，以紧凑布局启动
.\desktop\Ipc.Desktop.exe --ui supply-station --view sportmaster-history --layout compact --config .\desktop\config --data-root C:\IpcReference\sportmaster
```

省略 `--ui` 等价于 classic；合法参数值区分大小写。未知值、缺值、重复参数及其他业务使用 supply-station 会在创建业务/设备前拒绝。`--ui` 仅为桌面参数，控制台 `Ipc.Assembly` 不接受它。既有 `--view sportmaster` / `sportmaster-history` / `sportmaster-maintenance` 和 `--layout standard|compact` 保持有效。

点击“工作台 / 作业记录 / 设备与维护”只导航页面。“辅助窗口”继承所选UI和布局，具有自己的草稿与选择，继续共享同一个 Application；关闭辅助窗口不停止业务。主窗口关闭先停止各窗刷新、最多5秒等待在途快照和保护Pause，再调用宿主的停止预算。任一失败保留仍存活的窗口、资源责任和诊断，可在故障消除后重试。已关闭子窗是终态，不重新启动计时器；主窗仍在关闭时，子窗失败不能提前恢复轮询。

启动先展示真实阶段和诊断，失败保留可复制说明，不显示虚构进度或绕过Application门禁。桌面只读SQLite/文件与备份等服务调用由受等待的后台任务拥有，UI集合/投影回窗口上下文更新；原Busy、独立Pause和关闭排空继续有效。实际响应与长期负载仍须目标IPC测量。

## 实际页面与原型差异

新窗口保留原HTML的浅灰背景、白色卡片、顶部导航、蓝紫双栏、识别与任务信息组、内嵌历史。位置和设备来自实际配置；不使用原型中的固定工位/人员。字体只引用系统字体，不带字体二进制或浏览器/CDN运行时。

每位显示本位reader、最新观察分类/原因/时间、原条码、Admission/Operation、WCS请求、格口及主链责任；条码和标识保留字符串和前导零，完整复制。当前对象优先按位置投影的 CurrentOperation 匹配；不匹配时显示缺失，不回退成另一件。旧schema1/2明确显示单main兼容位，右位未启用，不把一件复制成双位。

“WCS格口已取得”“WCS明确拒绝”“WCS结果待核对”与等待、处理中、识别失败、失效采用文字及语义样式分别显示；普通RCS摘要不使用错误语义。最新无码、多码、非法码不创建新Operation，旧主链终结时不再把上件成功/格口当作本次结果；仍未决的原责任继续显示并受原守卫。格口仅在本作业WCS明确接受时显示，未知结果不能沿用上一件格口。格口取得不代表PLC/AGV/物理完成。框架中已有的称重、取图/上传策略保持原配置与守卫；本UI不新增相机预览或图库。

原稿的共享周转箱、波次/SKU数量、拆包、AGV状态开关和Dispatch/End Supply逻辑不属于当前Sportmaster。主屏用实际站位、修订、全局阻断替代共享箱横条；最新main的容量与附加上传/回执责任分别展示，继续原阶段携带本位检查说明；操作使用已有查看详情、核对原请求、继续未尝试原阶段、暂停/恢复。历史和维护复用原生页面及其分页/原件读取接口；classic和新布局初始进入历史/维护均读取对应数据，历史显示加载/空/失败/更多页和Operation→Request→Attempt/Reply层次，完整原JSON仍可复制。详情与所选原件身份一致，迟到旧查询不能覆盖新选择。

登录启用时使用已有认证接口；未启用时显示未启用。Simulation提供按位显式参考输入，需显式创建新实体身份；重复点击不自动发明新身份。Production隐藏该区域，缺真实能力仍NotReady。

## 小客户区与诊断积压

诊断原文不裁剪：顶部设备/附加责任最多132 DIP，全局状态/原因/错误最多96 DIP，均可局部滚动；最后成功读取时间独立常驻。字段继续局部滚动，双位恢复动作保持固定。

standard/compact按有效客户区切换真实重排，紧凑布局以800×420 DIP为最低目标，保留两位来源、当前状态、原码、格口及固定操作，详细身份/诊断局部滚动或折叠，不整屏缩字。根Grid有效客户区小于800×420 DIP时进入本窗保护，禁止新入场、继续原阶段和恢复；提供可滚动保护说明、最大化和重试软件暂停。进入保护只调用已有Pause，不取消在途请求。窗口初次超出工作区时最大化。恢复空间不会自动Resume：须取得有效暂停快照、点击“确认视图，保持暂停”，再核对现场并显式恢复新入场。保护Pause在主窗关闭时与刷新一起有界等待。Windows DPI、多屏及真实布局验收仍待执行。

## 实现、依赖与构建

- `src/Ipc.Desktop/DesktopLaunchOptions.cs`：严格解析桌面参数。
- `src/Ipc.Desktop/ParcelApp.xaml.cs`：预校验后创建一个runtime并选择UI。
- `src/Ipc.Application.Sportmaster.Desktop/Presentation.cs`：显式UI工厂。
- `SupplyStationWindow`：窗口、页面、500ms轮询、有界关闭及客户区保护。
- `SupplyStationPosition` / `SupplyStationTheme`：本位控件与资源；控件自己声明主题资源，创建时不依赖父树已经挂接。
- `SupplyStationViewModel`：可跨平台验证的真实UI投影与命令转发；位置对象和草稿保持稳定。轮询、命令刷新、暂停及配置确认均经同一快照读取扩展点。读取失败或超过3秒失效，原事实标旧并禁用新操作，最后成功读取时间常驻；成功读取恢复。每次快照读取分配单调请求ID，失败只被ID更大的成功读取覆盖；较旧读取即使先完成，也不能掩盖较新读取随后失败。失败之前的结果禁止重新发布，失败之后已挂起的新读取成功可以恢复，无需额外轮询。

沿用 .NET SDK10.0.402、WPF及已有锁定包，无新增生产NuGet依赖、原HTML、WebView、字体或SDK。

```sh
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -m:1 -nr:false
dotnet run --project tests/Ipc.Application.Verification -- . --sportmaster-ui
# 发布前先提交实际源文件及随包指南；输出路径必须全新
python3 scripts/package-application.py --application sportmaster --target win-x64 --output /new/immutable/sportmaster-ui --dotnet /pinned/dotnet
```

发布脚本在原选装闭包内包含两套WPF资源；ApplicationId、Views/Layouts、公共应用契约和业务部署配置格式均不变。release文件包含真实文件hash；随包 `SPORTMASTER-UI.md`、`SPORTMASTER-UI-WINDOWS.md` 和 `APPLICATION-START.txt` 说明启动和验收。

## 验证边界

跨平台 `--sportmaster-window-lifecycle` 使用实际窗口代码及事件/计时器替身验证关闭控制流，覆盖主子重叠关闭、停止失败重试、挂起刷新/保护Pause、子窗失败及5秒超时；它不执行WPF布局、原生Dispatcher或HWND操作。

原UI阶段验证结果及源码/候选身份见[历史结果报告](sportmaster-alternate-ui-results.md)；不能充当本分支新观察、紧凑重排、启动及维护改动的同版证据。本机为macOS，只能执行跨平台应用/投影测试和WPF交叉构建、发布/解包/资源检查。Windows窗口、DPI、焦点、触控和截图实际 NOT_RUN；真实SDK、WCS、实体/现场验收 BLOCKED。可执行的Windows原生烟测与后验步骤见[Windows清单](validation/sportmaster-alternate-ui-windows.md)。原型截图和BAML资源都不能当作Windows运行证明，D/RV历史不倒写。

组合增量：与RCS组件共用只读DestinationSummary，在既有每位诊断区显示任务/SKU/拆包格口及原始零值/未知字段，UnpackRequired明确显示本位等待检查。没有新增业务动作、图像展示或物理完成声明。版本/候选身份和当前组合验证见[整合报告](parallel-task-reconciliation-01.md)。

读取恢复时，仅清除本次状态读取错误拥有的横幅，其他业务消息继续保留；命令后读取与周期读取共用读序及失效判断。整合回归覆盖失败→恢复、乱序、窗口排空及RCS拆包投影。

审查03：两布局的快照读取失败/超过3秒未成功时，共用失效显示和维护动作保护；时效同时使用单调时钟与显示时间。运行实例切换清本窗旧选择，退役实例不能回写；同实例修订倒退明确失效。当前件严格按Position.CurrentOperation定位，旧格口明确标旧，不从历史/时间排序猜目标。轮询失败时软件暂停仍以已显示修订到Application做CAS，成功回执与快照是否恢复分别显示；刷新成功不自动Resume/重发。Windows原生交互、DPI及截图另行验收。
