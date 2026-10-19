# UX商业质量修复01结果

按用户“先修复问题，后面再验收”实施。已修复UXC-01～07中可由现有代码、合同和设计完成的部分，并交付可运行的独立Application、桌面接线与新参考媒介。**没有完成Windows原生或现场商用验收**；真实SDK/设备/权威Unknown处置合同不足的G-02仍按能力阻断，Production仍NotReady。

[PR #37](https://github.com/yuan2go/importer-pc/pull/37)（OPEN，未合并） · [工作包](../work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md) · [实施计划](../superpowers/plans/2026-10-19-ux-commercial-fix-01.md) · [证据入口](evidence/ux-commercial-fix-01/README.md) · [不可变产物](evidence/ux-commercial-fix-01/ARTIFACTS.json)。

## 固定身份和范围

- 开工和结束核验远端main均为`8f886f44bec01779318ec078a569500ab1beab56`；原工作区6个未跟踪资料入口保留，未覆盖并行工作。新隔离分支`codex/ux-commercial-fixes-01`，开工无开放PR。
- 产品/媒介固定源码：`c384c3bc4750bbaafbcc3b834d3b12aa126c3b41`。业务1.3.0、Application.Contracts1.9.0、Parcel DB7，SQL1～7未改；配置schema1/2/3。新参考包：能力2.2.0、reader1.3.0、RCS1.1.0，均Simulation/TestOnly，不覆盖旧身份。
- 14组冻结验证器来自同一产品提交。补充签名RCS归档夹具来自`b38a2d323ff21b7b5a7da798b8d3008fc75730d4`，仅修改测试回执处理；其全部生产DLL依赖逐字节取自冻结验证器，指纹见checks.json。后续提交只补夹具和证据，455个发布输入与产品提交的[字节对应](evidence/ux-commercial-fix-01/source-parity.json)全部一致。
- 执行环境为macOS 26.5.2/Darwin25.5 arm64，SDK10.0.402、运行时10.0.13，串行构建。只有隔离模拟输入、独立本地参考接收器及loopback RCS；未连接生产设备。实际WPF截图0张。

## 已修复的行为

|原问题|最终实现及证据|保留的边界|
|---|---|---|
|UXC-01 本次无码仍展示上件成功|Application新增独立Inputs投影，原Observation、原ReaderBinding、结果分类与Operation关联进入既有parcel_audit同一事务。无码/多码/非法码不创建实体或发送；旧主链已终结则CurrentOperation为空，原记录仍可查。未决原件仍显示并阻断，不因失败观察解除。|来源/时效/健康通过才推进序列；旧epoch/sequence不能覆盖当前投影。重启按持久seq重建有界缓存；换DeviceId或同ID换Revision不沿用旧终结格口。重复旧A不会把当前B结果和A观察时间拼接；两布局清楚分开最新输入与原件。|
|UXC-02 状态缺少语义区分|正常、处理中、接受、拒绝、Unknown和失效分别有文字、图标及色彩；正常RCS结果摘要不再恒用错误色。|接受、协议回执和物理完成仍为不同事实，不把视觉接受色当设备动作完成。|
|UXC-03 默认schema3只剩JSON|维护页加载全部reader、投递位、实例库存、角色与本位秤/相机；按已安装精确提供者及签名依赖闭包构造完整候选。保留无关字段、原省略默认与参数键顺序。校验、保存Pending、受控重启Effective分开。|双窗仍用加载时CAS；JSON/表单显式切换。保存A时继续编辑B，B保持未保存；非法JSON转表单失败保留原草稿；导航不重新读取覆盖草稿。安装/卸载沿原Store。恢复操作ID仍是明确高级入口，当前接口不枚举安装日志，未造选单。|
|UXC-04 小有效区没有真实重排|两布局重排关键码/格口、状态、来源和固定操作；诊断/原件详情局部折叠滚动，紧凑导航改菜单；目标800×420 DIP，不使用整页Viewbox缩字。|低于最小区仍请求软件Pause，恢复空间需有效暂停快照及人工确认，不自动Resume。1093×614、911×512等价几何的守卫验证通过；实际布局/DPI/触摸尚待Windows。|
|UXC-05 启动及页面状态不完整|创建运行时前显示真实启动阶段，失败保留可复制诊断和停止重试；初始history/maintenance入口读取对应数据。历史有加载/空/失败/翻页和本地时区时间，结构化原件→请求→尝试/回执及完整JSON。|历史/详情有代次及运行实例围栏，迟到读取不覆盖当前选择；导航保留选择和配置草稿，不另建runtime。|
|UXC-06 RCS本地启动被解释为上游就绪|维护健康明确显示“客户端已启动，上游未确认”和来源证据。|未新增未经确认的health/query接口，POST不冒充查询；Unknown不盲重发。|
|UXC-07 UI服务调用可能同步阻塞|纯服务调用在被等待的后台任务执行，属性/集合在捕获的UI上下文更新；跟踪服务及整个动作直到关闭排空。独立Pause仍可执行，超时保留所有者和重试。|受控阻塞服务证明调用线程释放及UI上下文更新；未测目标工控机响应时间、帧率、长期稳定性或触摸体验。|

核心路径：[观察契约](../../src/Ipc.Application.Contracts/ParcelInputs.cs)、[持久/显示投影](../../src/Ipc.Application.ParcelFlow/ParcelInputs.cs)、[实际观察准入](../../src/Ipc.Application.ParcelFlow/ParcelRuntime.cs)、[结构化维护](../../src/Ipc.Application.ParcelFlow.Desktop/StructuredConfiguration.cs)、[共享展示](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs)、[服务所有权](../../src/Ipc.Application.ParcelFlow.Desktop/DesktopServiceCalls.cs)、[实际启动窗口](../../src/Ipc.Desktop/ParcelApp.xaml.cs)。原HTML、CSS/JS、tokens、PNG和历史D/RV/审查结果均未修改。

Sportmaster保持两位独立相机→WCS→格口展示，不加PLC、图片留存或共享箱；DirectPlc完整UI仍暂缓。三个主流程没有合并。

## 实际验证与失败保留

冻结源码的14组包括：新增真实输入/展示/完整配置，Sportmaster UI及窗口排空，commercial-quality，audit03的输入/快照/维护，Application职责、配置确认、版本、RCS和原件恢复P2。覆盖了完整参考主链、原请求/hash/尝试次数、独立右位、NoRead重启、来源围栏、换绑、事务回滚、双窗CAS、后台阻塞及迟到读取。全部返回0；[逐组命令身份、时间、日志hash和原输出](evidence/ux-commercial-fix-01/checks.json)可复核。

三种Application的WPF交叉构建通过；三个portable实际ZIP解包并启动主链/重启原件/有序停止通过。另在实际选装ZIP内完成双签名reader、签名WCS安装/配置/激活/失败回退/安全卸载，签名RCS两位真实loopback HTTP原报文/原回执核对、拆包零剩余、重启/查询/重连不重POST。补充签名能力链及修正夹具后的签名RCS持久编号/归档/备份范围退役通过。

保留的负证据没有删除或改成PASS：

1. main反例确实失败于“NoRead后上件格口仍当当前”。修复后同一核心不变量及扩展回归通过。
2. 开发过程发现并修复：未编辑参数被重排、草稿所有权与非法JSON半切换；旧测试的同步读取假设加明确调用开始屏障；错误的合成ID/schema1 fixture按实际规则修正。没有降低业务校验或删除关键断言。
3. 补充签名RCS首轮未归档：只读DB显示暂停/维护认证成立、两件终结且图片已释放，但无归档审计或归档命令。结合控制流可推断旧夹具忽略了瞬时守卫拒绝；**旧回执未记录，未补造其原文**。b38a2d3只对明确的归档守卫拒绝有界重试，并要求最终Persisted。产品和候选未改。
4. 选装媒介旅程首次遇配置前置守卫拒绝，第二次遇新增日志分支把History页误当带Status回执的KeyError。相应失败保留；最终脚本只重试精确前置拒绝，保留原配置CAS/候选，逐次记录回执。新隔离根完整旅程通过，没有重试未知外部投递。
5. P2最初经`dotnet run`启动时旧子进程夹具重复传DLL路径；使用明确的`dotnet <verification.dll>`后完整恢复断点验证通过，未修改业务或弱化断点断言。

文档归档校验仍为FAIL：本次基线实测268项，现为267项，修复manifest校验文件命名/校验值后净减少1项，新增0项。余项为继承的原UI/资料问题；没有加豁免、改原件或把整体标PASS。[差额](evidence/ux-commercial-fix-01/archive-delta.json)。Python兼容声明17项及随包文档2项检查通过。

## 交付与后续验收

本地不可变候选共5个ZIP、3个独立扩展包及各自构建身份。逐文件SHA256/ZIP CRC、实际deps契约版本、同源码随包指南、可选包身份、win-x64 PE架构和无私钥检查通过。逐包大小/hash见ARTIFACTS.json；二进制只存本地.artifacts，未创建远端Release或替换旧包。此PR用于代码审查，未自动合并。

后续按既有Windows06完整显示矩阵及[UI后验清单](validation/sportmaster-alternate-ui-windows.md)核对真实客户端：两布局的正常/等待/拒绝/Unknown/断连/恢复、长码/长原因、800×420及1366/1920的100/125/150%缩放、固定动作可见性、焦点/键盘/触摸、初始页、配置草稿、关闭及普通账户安装保护。补真实目标硬件下输入连续性、时延和长稳测量；本次没有以模拟或交叉构建关闭这些项目。

G-02仍需每个Application已采纳能力的真实SDK/驱动、正式WCS/RCS合同、部署权限及权威Unknown核对/处置依据。缺这些输入的能力继续明确阻断，不能用本次UI修复授予商用放行。
