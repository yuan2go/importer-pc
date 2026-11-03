# Sportmaster 工作台信息精简

[工作包](../work-packages/WP-IPC-SPORTMASTER-CLEAR-WORKSTATION-01.md)。本轮只调整 Sportmaster 默认 WPF 工作台，继承 PR60 的业务和安全守卫。原 HTML/CSS/JS 为 SS-UI-VIS-001 v1.1 模拟设计，当前实现仍是扫码到 WCS 格口，不补造箱号、波次、数量或物理完成。

## 视觉与交互

使用项目 Impeccable 4.7.0（上游 `46b822f7dda9f7dba799a9c7776fdc19b3a93559`）的 Operate、WPF 适配及 critique/layout/clarify/polish 流程。已读取原 HTML、分离资源、同源截图及完整交接；WPF 查询未命中时使用本地 WPF 规则和实际原生证据，没有把网页/CDP 扫描用于验收。

|问题|本轮处理|
|---|---|
|工程说明、设备编号、时间戳及重复提示占据主屏|主屏保留投递位、业务状态、条码、有效格口、操作与必要阻断；来源明细放入作业详情或“更多 → 站位与设备诊断”。容量诊断仍保留。|
|文字太小、层级不清|位标题 22 DIP、业务状态 26 DIP、条码 30 DIP、格口按长度 24–64 DIP、主要标签及按钮 16 DIP；系统字体及原语义颜色。|
|格口随剩余高度无限拉伸|内容顺序明确、格口高度有界；标准布局纵向展示，紧凑布局条码和格口并列。|
|说明回执跨页残留|正常等待读码/模拟排队说明不再常驻；从维护页返回不显示配置读取说明。操作失败、拒绝、冲突及实际阻断继续可见，完整 Message 保留。|
|异常长原因挤掉当前数据|最小窗口限制原因区高度，可局部滚动；“原件保留 · 结果待核对”保持可见，完整原件身份在详情；条码可完整选择复制。|

恢复和原请求操作仍由应用契约守卫。没有改变业务状态、连接寿命、快照权威、准入、发送、接受或物理完成语义。工作台“格口已取得”不表示物理完成，详情保留此边界。原 HTML 与旧 RV 不因本轮局部优化改写为通过。

## 验证记录

Windows 11 22631 / x64 / 125% DPI，SDK 10.0.402。开发构建 0 警告、0 错误；原生视觉旅程及新增默认工作台极端窗口检查共 95 项 PASS：双位独立请求、Unknown 原请求核对且不重发、必要登录/阻断可见、焦点/选择/滚动/草稿、长内容、拒绝/失效、800 × 420 有效客户区和不足空间暂停保护。图像来自真实 WPF 渲染，数据/身份/端口为显式合成。

初次视觉夹具发生定时刷新覆盖显式读取的竞争，现等待预期投影实际到达后断言，保留断言与超时失败。大窗口夹具曾要求本显示器容不下的 900 DIP 客户区，现最大项按工作区预留边框，仍逐项核实精确尺寸。另补充字段在局部滚动视口内的可见性断言，实际修复了长异常原因遮挡条码的问题。

扩展 `--commercial-productization-native` 检查发现未改动的 classic 兼容工作台在 800 × 420 下固定操作越界；其原 FAIL 保留，不声称全套兼容 UI 已通过。本轮默认 supply-station 单独完整验证，不删除 classic 断言。

冻结运行源码及夹具：`0e79e73bdb6d7a1702373963295b192a803f28e7`，已继承 PR61 文档整合。发布后的真实 DLL 以同源夹具执行 **100 项 PASS**，包括首配和完整默认工作台旅程；验证前后介质库存不变。[冻结结果](evidence/sportmaster-clear-01/frozen-result.json)、[日志](evidence/sportmaster-clear-01/frozen-visual.log)、[原生主屏](evidence/sportmaster-clear-01/native-workstation.jpg)、[极端最小窗口](evidence/sportmaster-clear-01/supply-station-client-800x420-unknown-rejection.png)。

通过 computer-use 原生输入实际最大化窗口、点击条码后 Ctrl+A 完整选择 `000123456789`、展开详情、切到作业记录再返回，确认展开状态、原件和选择保留，再收起并关闭。截图为目标 HWND 的真实捕获；与测试中的 RenderTargetBitmap 图分别存放。没有访问真实设备或修改机器政策。

实际发布 EXE 以 `--setup` 和隔离数据目录启动，正常进入步骤 1 / 4 后关闭；[启动截图](evidence/sportmaster-clear-01/published-startup.jpg)。没有执行安装、UAC、数据迁移或现场业务；这只证明发布启动冒烟。

本地 win-x64 候选：`D:/w/sm-clear/.artifacts/sportmaster-clear-media.zip`，104298590 字节，SHA256 `c9ac614d885c465dee3cb2d1922c9c67e0315b82e6749688d697afee3946f060`。库存 SHA256 `6ABBC2DD9B4121274C5DDBADA194403A77ED21B915B6CAB56F228916CA522D20`；业务 1.4.0。候选不上传 Release，版本与资格仍按既有规则。[介质记录](evidence/sportmaster-clear-01/MEDIA.json)与[复核脚本](evidence/sportmaster-clear-01/verify.py)保存精确身份。

## 运行

```powershell
D:/tools/dotnet/dotnet.exe build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -p:Application=Sportmaster -m:1 -nr:false
D:/tools/dotnet/dotnet.exe tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/sm-clear --visual-productization --interactive
./scripts/verify-commercial-wpf.ps1 -Media D:/w/sm-clear/.artifacts/sportmaster-clear-media -Output <新证据目录> -Dotnet D:/tools/dotnet/dotnet.exe -VisualProductization -Interactive
```

这是显式合成的原生预览，不连接现场。正式安装和默认启动继续使用现有受保护入口，缺失 Production 必需能力仍拒绝。未进行真实 SDK/设备、现场、物理触控、混合 DPI 或安装/UAC 验收。

## 并发整合补修（本轮证据另记）

#62 P2：单条已识别码的校验拒绝显示“校验未通过”，无码和多码分别显示，不改变准入事实及原作业归属。只读诊断 TextBox 改为显式 OneWay，并增加实际打开及绑定断言。开发原生视觉旅程保留原断言并新增四项，0 警告/0 错误，进程正常退出。

初始 CI 39365283961 原日志显示 runner 实际客户区宽 1028 DIP，无法容纳要求的 1100 DIP；首次尝试为 runner 设置1920×1080模式，但会话无可枚举显示模式，39374018434明确失败；未将其记为通过。后续夹具只在原生客户区受桌面边界限制时，为测试 HWND 提供更大的原生 backing surface，保留产品最小值和全部精确客户区/内容断言；不改系统显示模式、不声称物理屏幕验收。#64 初始失败为旧夹具定时刷新竞争，#62 原有等待实际投影的修复继续保留。最终组合以续轮整合报告及新同源冻结证据为准。

扩展全旅程还实际修复 classic 800×420 固定操作越界：紧凑状态区局部滚动、命令目标摘要有界且完整内容保留在提示/原件；默认UI在登录与大量原因下约束紧凑头部和留白，条码至少36 DIP，格口/条码在本位视口内。保留所有原断言及 FIELD 几何原始数据。合成扩展入口的旧schema关闭夹具继承相同合成身份/端口，普通入口仍使用原生产身份门禁。初始失败记录与最终精确源码验证见续轮报告。
