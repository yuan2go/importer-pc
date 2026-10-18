# Sportmaster 第二套客户端 UI 设计草案

状态：用户已于2026-10-18确认设计并授权衍生WPF代码提交当前公开仓库；原HTML不上传。

## 目标与基线

用户要求给 Sportmaster 增加本地 `docs/供包台.html` 中的 UI，并允许通过客户端参数选择。保留现有 UI 和默认启动兼容性；新旧 UI 使用相同应用契约和同一个业务实例。

- 开工远端 main：`b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d`。
- 隔离分支：`codex/sportmaster-alternate-ui`；不修改主工作区及其他 PR。
- 输入 HTML SHA-256：`eb52c3a1e965bb0f52f96379d8ec031c4954407998cae3077e6653a284b990f8`。输入是未跟踪本地原稿，保持原字节，不自动加入 Git。
- 视觉基线同时参考 SS-UI-VIS-001 v1.1 的 README、GALLERY、原 HTML、分离 CSS/JS、完整 UI 交接与主屏 PNG；原型历史业务语义不覆盖架构22。
- 当前 Sportmaster：left/right 两个独立投递位、两个 reader；每位扫码→WCS 格口。没有共享周转箱、SKU数量拆包或 AGV 发车业务。
- 并行开放 PR：#23 采集生命周期、#24 架构质量闭环、#26 审查02修复；实施前重新检查文件重叠，不顺带合并其未合并逻辑。

## 方案比较

1. **推荐：在 Sportmaster.Desktop 中新增原生 WPF 窗口。** 可还原原稿的顶部导航、蓝紫双栏、分组卡片和页脚，同时将真实事实绑定到现有契约。需要实现窗口导航与生命周期接线，但不引入第二业务所有者。
2. 只在现有 ShellWindow 内替换 Workstation。共享外壳改动较少，但原稿完整页面结构会被现有大块头部和底部操作区改变。
3. 使用 WebView 承载原 HTML。增加 WebView 运行时、桥接和离线依赖，还必须重写模拟逻辑；与现有原生 WPF 路线相比成本更高，本次不采用。

## 客户端选择

```text
Ipc.Desktop.exe --ui classic --config <config目录> --data-root <数据目录>
Ipc.Desktop.exe --ui supply-station --config <config目录> --data-root <数据目录>
Ipc.Desktop.exe --ui supply-station --view sportmaster-history --layout compact
```

- 不提供 `--ui` 等价于 `classic`，保持现有默认行为。
- `--ui` 仅是桌面展示选择；不写入业务配置、不改变 ApplicationId、存储路径或既有请求。
- 严格拒绝缺值、重复或未知 UI 参数；其他 Application 不接受 `supply-station`，在启动业务/设备前失败。
- `--view` 继续选择本业务已经声明的工作台、历史、维护页面，`--layout` 继续使用 standard/compact。
- 不新增业务页面 ID；两套 UI 都实现现有三个页面，因此不必修改 ParcelRuntime.Presentation 或公共契约指纹。
- 辅助窗口继承当前 UI 选择和布局，创建自己的 ViewModel/控件，继续共享原 Application。

## 视觉与行为映射

采用原 HTML 的浅灰背景、白色顶部与信息卡、蓝色左位/紫色右位标识、两等分工作区、内嵌历史和底部状态区。标签采用既有客户端中文操作词，稳定设备 ID 和位置由配置取得；不得写死原稿 ST-A01/ST-A02 或 Li Ming。长条码可完整复制和换行。

|原型区域|本次实际接线|
|---|---|
|顶部品牌、用户、系统状态|Sportmaster 标识、生效站位、当前登录会话、真实快照与健康；未启用登录时如实显示|
|两侧工位标题和蓝紫分区|固定 left/right 身份，从 Topology/Positions/Devices 获取 reader 映射及状态|
|Product Barcode|对应位置的原 ParcelCode，只读字符串，保留前导零|
|Task Info / Target Slot|原 OperationId/AdmissionId、请求身份和 WCS Slot；只显示本位置本作业事实|
|状态与处理结果|处理中、明确拒绝、结果未知、格口已取得分别呈现；格口取得不显示物理发车成功|
|Scan History|按位置筛选应用快照中的作业；指明为当前保留记录，不承诺不存在的全量分页接口|
|共享箱、SKU、波次、数量、AGV|当前契约无这些业务；移除交互入口，不补模拟字段，以站位/配置/全局阻断信息替代共享箱横条|
|Clear / Dispatch / End Supply / Unpack|不映射为本地清空或伪成功；使用已有查看详情、核对原请求、继续未尝试原阶段等动作，并标明原对象及应用守卫|
|相机/AGV可点击状态开关、Space随机扫码|不进入业务 UI；Simulation 的参考输入集中于显式展开区，以 EmitReferenceAt 指定位置，Production 隐藏|
|历史/维护辅助功能|复用已有 HistoryPage、MaintenancePage 与应用接口，导航不能重建 runtime 或丢失输入草稿|

应用投影使用 `ParcelSnapshot.Positions`、`Devices` 和 `Operations`。当前作业优先按 PositionSnapshot.CurrentOperation 匹配；完成后按同一 PositionId 的最新记录显示。旧 schema 的单 main 位必须明确显示兼容模式，不能复制同一作业冒充两个独立位。

## 实现边界与文件职责

- `src/Ipc.Desktop/ParcelApp.xaml.cs`：解析并预校验 `--ui`；在现有 Application 创建后选择窗口；只有主窗口拥有 Stop/Dispose 回调。
- `src/Ipc.Desktop/Ipc.Desktop.csproj`：将可跨平台测试的桌面参数策略源文件纳入构建，继续条件引用所选 Application UI。
- `src/Ipc.Application.Sportmaster.Desktop/Presentation.cs`：显式工厂选择 classic 或 supply-station，传入同一 `IParcelApplication` 和主窗口停止回调。
- 新 `SupplyStationWindow.xaml/.cs`：新外壳、页面导航、定时快照刷新、辅助窗口、忙态、关闭失败保留窗口及资源责任。
- 新 `SupplyStationPosition.xaml/.cs`：两位复用同一控件模板；实例只拥有本窗展示状态，按位置呈现数据和发起命令。
- 新 UI 投影/选择策略类：只做现有契约到展示字段的转换，不解释或修改业务规则；源文件链接到跨平台验证工程。
- `tests/Ipc.Application.Verification`：补充参数、双位投影隔离、未知与拒绝、旧schema兼容、两套VM共享同一 Application 的定向验证。
- `scripts/package-application.py`、随包 APPLICATIONS/启动说明：包含两套 WPF 资源及客户端示例，按实际源提交生成 release/hash。
- 当前 INDEX、运行文档、UI映射、manifest：只更新本包涉及的现行入口；旧候选、D/RV、HTML原件与历史验收保留。

不改 SportmasterFlow、ParcelRuntime、阶段执行/恢复、SQL/迁移、WCS协议、设备会话或许可规则。若发现现有契约不能支撑必需显示，先记录具体差距，不在 UI 内造第二状态机。

## 错误与资源行为

快照读取失败显示失败原因和最后更新时间，保留旧快照并标为过期；不把旧状态显示为当前就绪，不根据失败自动重发。异步刷新采用代次和 Revision 防止旧结果覆盖新状态。页内草稿和键盘焦点不随轮询重建。

每个窗口拥有自己的刷新计时器并在关闭时停止；辅助窗口关闭不停止业务。主窗口停止失败继续持有窗口与诊断，不将失败吞成退出成功。新窗口复用既有命令入口/原作业ID及版本，禁用原因与应用结果持续可见。

## 验证与交付

1. UI选择策略：默认/两个合法值、未知值、缺值、重复、跨业务拒绝，且校验先于业务创建。
2. 实际 UI 投影源文件：左右交错、同条码不同实体、右位故障不覆盖左位、拒绝/Unknown不保留旧格口、新快照代次、旧单reader配置。
3. 共享实例参考链：两套展示模型读取同一 RuntimeId，经真实 Sportmaster 参考应用输入两位合成观察，不增加业务实例、请求或设备连接。
4. 必要 Sportmaster WPF 编译及受影响契约验证；不为此改写关键断言，不运行无关全量。
5. 实际 win-x64 选装发布，解包检查两套UI资源、启动说明、release/hash；portable 只验证参考业务，不充当 WPF 运行证据。
6. Windows窗口、焦点、DPI、触屏和真实设备无法在当前Mac运行时明确记为 NOT_RUN/BLOCKED；提供可执行Windows后验步骤，不把HTML截图标成新WPF截图。
7. 用户确认公开范围后，开发分支提交推送并创建 main PR，不合并，不带入原HTML、客户PRD、凭据、SDK/字体或生产数据。

## 已确认的授权

1. 采用推荐原生 WPF 方案：按原稿视觉结构接现有 Sportmaster 业务，默认 classic，通过 `--ui supply-station` 选择新 UI；原稿未被 Sportmaster 支持的箱/数量/拆包/AGV行为不进入本次业务。
2. `docs/baseline/PUBLICATION.md` 把原产品HTML及衍生资产列为受限。本次用户进一步明确允许本次由该原稿衍生的WPF UI代码提交当前公开仓库。原HTML仍保持本地不上传。

本文件为编码前确认的设计；实际完成情况以运行报告、源码提交及PR为准。
