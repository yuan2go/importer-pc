# ARCH-UI-ALIGNMENT-01 实施与交付

工作包：[WP-IPC-ARCH-UI-ALIGNMENT-01](../work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)。基准 main `2b8b35b6025ed87438f27c7c821260f4740befba`（PR #3），2026-10-16。本包在既有 C#/.NET10/WPF/SQLite 参考产品增量实施；不是客户现场迁移。实际源提交、构建/回归/发布哈希与 PR 见[检查记录](alignment-01-checks.json)。H04及RUNTIME-03原记录保留形成时证据。

## 当前实现与所有权

|目标|实现|证据边界|
|---|---|---|
|可组合基座|Platform 的 ProductDefinition/ModuleRegistration 受控注册，结构、依赖、版本、模式、路径、哈希校验；ReferenceProduct 显式绑定已安装工厂|两个不同测试注册装配通过；只交付 supply 一条参考业务，不动态扫描未知 DLL|
|业务与设备边界|SupplyConfiguration 拥有双通道/模拟规则；DeviceCapabilities 在 Platform 不引用 Supply；供包专有 IRequestContract/集成端口留业务|旧 StationConfig 字段与默认值保留为兼容 DTO，不是 Platform 准入规则；SIMULATION-v1 请求形状保持|
|完整任务链|独立 SyntheticSemanticFixture→持久回执→ContextSnapshot/TaskSnapshot→入场箱快照→版本投影→主屏/历史/结构化详情|箱波次、Item 波次互不代用，来源/版本/证据/取得时间保留；旧数据 null 显示“未取得”；不宣称真实 WCS 字段|
|多页面/布局|六个受控 ViewId 实际选页；启动覆盖→用户偏好→发布默认；布局修改保留当前 View|双视图入口共享 Runtime/Application/发送器，命令幂等与单次发送经过核心验证；实际双窗口操作待 Windows|
|维护配置|驱动允许名单、端点/目标版本、容量/超时/低水位及合成参数表单，差异/校验/状态/生效时机/失败反馈|后台重检权限/规则；站点ID/模式固定；Valid→Pending→重启 Effective，失败 ActivationFailed 保旧，原请求固定原目标|
|专业操作工作站|当前箱/波次/唯一接受量→双通道条码/任务/数量→原因/下一步→固定操作带→最近作业；中文状态、图标与颜色、禁止原因/执行中/防重复|工程事件/故障/角色切换独立 Simulation 页；Simulation 标识始终可见；无 Windows 截图则不宣称视觉验收|
|追溯与恢复|Operation→Request→Attempt→Response/Audit 树，原始报文仅折叠高级诊断；关闭专属详情、H04 原请求核对/Prepared本地终结/过载版本确认保留|已本地终结、实体待处置、接受与物理完成分别显示；不增加释放预留/实体或重新授权命令|
|后台与读取|StationDispatcher 单后台消费者，有界普通队列和独立控制容量；存储/维护/历史/备份都走同一所有者；快照只读，带 ProjectionVersion/GeneratedAt|慢 SQLite 实锁和T1注入验证调用异步返回、事件保留；过期3秒/读取失败撤销UI守卫，命令仍查当前状态|
|页面生命周期|每窗独立草稿/读取取消源；关闭只取消等待；详情代次拒绝迟到覆盖；布局重排原控件而不重建|焦点/选区/滚动/扫码需要 Windows 操作验收；局部输入接收 Enter/Tab，危险确认窗不接收扫码 Enter|

### 并发与 H04 继承

网络等待不占协调器或数据库事务。所有数据库路径从固定后台所有者执行，不用无界 Task.Run 争用共享连接；普通命令满立即关准入并递增证据版本，拒绝反馈共用一个合并写入任务/容量1信号，不为拒收洪峰建立无界控制等待队列。关键控制有独立16容量，停止排空/失败诊断有生命周期所有者。销毁后迟到命令明确 NotReady。

快照读取不再调用 SyncAlarms；状态变化与独立500ms维护脉冲更新报警。设备健康回调入独立有界健康队列，不等待 SQLite。设备观察仍经原有有界队列；已接纳事件依序排空，不为流畅展示静默丢弃。H04观察拒收的 marker 保留同步 WriteThrough/Flush 持久证据，**该文件写入可能受磁盘时延影响**；不在回调做 SQLite/网络工作。UI模拟源注入也经过专属有界后台输入，不在UI刷盘。真实SDK回调/背压和资源生命周期仍待联调，不能由模拟源推定通过。

过载决策短锁不持 SQLite；最终 handoff 持久化期间若新增故障/过载，提交后复检抑制网络，将原身份保守标 Unknown，绝不把已经持久化的 TransportHandoff 降回 Prepared 或允许本地终结。原审计与尝试继续保留。停止先建立内存停止屏障；持久化失败仍停止发送器/来源并保留故障，不能因停止写库失败继续采集/发送。

### 版本与兼容

发布版本 `ALIGNMENT-01`、存储格式 `2`、外部参考合同 `SIMULATION-v1` 分别记录。schema1 原 SQL 与 SHA256 `D4CED2B811DFF4D3D66DC66C431B9DCC8C75F491CA30F10A2E5507AAD741C981` 未改；新增 schema_history id2 和 storage_format(version=2,minimum_reader=2) 前向门槛，未知迁移/格式/缺失证据拒绝。旧程序已有 id<>1 拒绝机制，不能直接读本包新库。新增语义响应为可选字段，null 时不输出，旧v1响应重新编码字节保持；原请求不重序列化、不增加任务字段来伪装客户协议。

备份恢复先检查源完整性/迁移/格式，再复制到新根并持久隔离；本包可读原schema1备份并前向迁移，也可恢复匹配格式2。回退H04/RUNTIME-03须使用升级前schema1一致备份、新根、旧程序恢复隔离，并核对原外部账/实体；禁止拿本包格式2库试旧EXE。

## 显示适配与 Windows 待验

`app.manifest` 声明 PerMonitorV2（[Microsoft DPI manifest依据](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)）。启动窗口按有效 WorkArea 限制初始尺寸；客户区测量是 WPF DIP，不按物理分辨率猜测。标准双列，宽<1100或高<600 DIP自动单列；紧凑显式单列；历史双栏→上下，工作内容滚动但主操作固定。最小 **800×420 DIP（ClientArea扣除窗口边框/内外边距后的有效测量）**，不足显示阻断覆盖层，后台保持运行。长条码可选择/水平滚动，原因/任务换行，长状态/反馈独立限高滚动。

原 tokens 的1280×680为历史设计假设且字节保留；本包扩展小有效区布局，不声称旧假设已通过或原HTML已修复。以下均 **NOT_RUN**：

|物理分辨率|100% / 125% / 150% 标称DIP（尚未扣边框/任务栏）|实际验收|
|---|---|---|
|1920×1080|1920×1080 / 1536×864 / 1280×720|每组均需同版本独立截图和操作记录|
|1366×768|1366×768 / 1093×614 / 911×512|最小组尤其核对有效区、任务栏、固定操作带和输入焦点|

按[Windows执行清单](alignment-01-windows.md)逐项检查，不以交叉编译、HTML重绘或这张推算表替代。实际有效区小于最小时应明确阻断，不缩放到无法点击以假装兼容。

## 验证与剩余条件

适当基线：build PASS、原24组风险回归 PASS。增量验证覆盖两种装配、旧报文/schema迁移、完整任务链、唯一接受量、慢SQLite实际锁/注入、投影读取不写报警、双视图命令与单发送器、控制队列饱和、迟到命令、停止失败资源停止、授权提交中的新过载、H04九组。最终结果与失败修复过程见 checks，禁止将一个局部PASS扩大成现场验收。

已实现待验证：WPF真实启动、正常/拒绝/Unknown/拆包/配置/恢复、多窗口、焦点/选区/滚动、6组分辨率缩放、提示声/触控。受阻：真实 SDK/WCS/数量/拆包/实体/权限及旧工程迁移，Production继续 NotReady。历史D/RV登记不改、受限原稿/图像/SDK/字体不随包。未完成的 Windows/真实接口验收不归入已验证。

构建/运行/发布和回滚详见[运行说明](operations.md)，实际包文件及哈希见 evidence/alignment-01 与检查记录。开发分支提交并创建 main PR，不强推、不自行合并。

## 本次发布回执

代码提交 `712446ae262c6b269bd6576ad5c17784a644c7d7`；自包含包 `ALIGNMENT-01-712446a-win-x64.zip`，119023233 bytes，SHA256 `1089c45f294ead68154d92b26217b9fd3c18bf86089c4f745cdc43ed5c7a8aeb`。813个包内文件校验、模块哈希、三个Windows PE入口、嵌入PerMonitorV2声明和ZIP CRC均PASS；没有数据库、凭据或字体文件。发布核心原生运行冒烟7项PASS，Windows执行NOT_RUN。二进制通过单独发布资产交付，不进Git源码。

固定版本运行：解压后 `./station.ps1 -Action Fixture`，另一窗口 `./station.ps1 -Action Init`、`./station.ps1 -Action Run -View workstation -Layout standard`。Windows首次运行与全部操作仍按清单验收，不能由包检查推定通过。

[main PR #4](https://github.com/yuan2go/importer-pc/pull/4) 已创建，保持OPEN未合并。Simulation包与校验文件已上传到草稿发布，目标为上述代码提交，远端ZIP digest与本地一致；草稿未公开发布，不等于生产/现场放行。
