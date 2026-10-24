# TYPOGRAPHY-01 · 原生字体与排版交付

2026-10-24。现行规范为[字体与排版](../design/typography-01.md)，来源、许可和官方文件哈希见[字体清单](../../src/Ipc.Presentation.Wpf/Fonts/NOTICE.json)及[原始 OFL](../../src/Ipc.Presentation.Wpf/Fonts/OFL.txt)。本包继承 PR97 HIERARCHY-02；没有合并其他 PR、安装应用或发布正式介质。

## 基线与实现

开工及交付前复核 main 均为 `c5ac51d`；PR97 仍开放，head `37b27a9`。隔离分支 `codex/typography-01` 从该 head 衔接，`e5ed46d` 纳入 main 的新审查文档，未覆盖原工作树的四份未跟踪原稿。PR97 是本包的前置依赖；主分支 PR 会包含该依赖的提交，应先处理依赖，再核对最终差异。

实现提交 `9e6c628` 新增共享 `Ipc.Presentation.Wpf`，所有目标 XAML、动态窗口、模板及 SizeChanged 使用同一套界面/代码字体和语义尺度。常规/中等字重分别对应实际 Regular 400 / Medium 500，不借用设计稿 WOFF，不安装全局字体。两个 OTF 合计 32,960,996 字节；WPF 资源程序集约 31.5 MiB，发布时字体已在程序集内。OFL 与 NOTICE 同时嵌入并复制到离线 `licenses/`；交付依赖清单新增 `BundledFonts`，哈希不符会拒绝出包。

`a6065f0` 修复首次设置密码的空反馈占位，日期与管理字段标签统一为14；保留原来的默认窗口尺寸。正文、输入、普通动作16，说明14，格口64/44、条码28/24按角色使用，低频技术元数据12。长格口按长度降为40/28或28/24；有限空间内可换行、滚动、选择和复制完整值。没有对字号基准作额外缩小。

默认供包台保留格口优先和空位隐藏。经典窗口采用格口/条码并排的局部排版，隐藏重复的正常状态，但异常仍显示；有效结果还必须匹配原观察的设备、周期、序号和准入身份。没有改变 Application 状态、命令守卫、原请求报文或责任。

查询行统一40、表头36；时间单行显示，详情保留原时间及偏移。日志摘要将原文换行显示为分隔符，原文、上下文和导出保持。最小窗口展开日志上下文时暂时收起关键词条，保留范围、操作、选中上下文和至少一条完整结果；关闭上下文立即恢复原控件与草稿。经典宿主低频折叠栏减少留白；关键操作仍可达。未重建连接、业务实例或参数候选。

## 证据与边界

证据总表、原始日志、资源核验及同场景前后截图见[原生证据](evidence/typography-01/README.md)。每组记录各自源码；后续文档提交不倒写前一次运行身份。

本机 Windows 11 23H2 x64（22631）、.NET 10.0.13，实际系统缩放125%。使用真实 HWND/Dispatcher 和隔离合成端口/数据；标准、紧凑及800×420、1092×614、1366×768等客户区均保留日志中的实际DIP值。窗口截图不是现场设备验收。

原生覆盖正常/等待、Unknown/Rejected/Conflict、失效/未准入、长条码与格口、选择/滚动、参数输入/错误/保存、记录/日志展开、管理与安装/首次配置。保留 PR97 的查询空间、权限、草稿与原请求断言；增加实际字体字形/文件/字重、经典条码可见区域及最小日志上下文结果行检查。没有为字号常量建立镜像测试。

100%/150%实际系统缩放、目标工控机、真实键鼠硬件、实际安装/UAC、SDK/相机/RCS、生产数据和现场联调均为 **NOT_RUN**。安装夹具文件名中的 `125-equivalent`、`150-equivalent` 是旧夹具的逻辑窗口名称，不能当作改变系统缩放的证据。没有重跑无关长稳或设备联调；既有全部 CI 门禁保留，冻结介质的任务层级门禁一并运行实际字体解析检查。

## 复现

在 Windows 11 和仓库指定 .NET SDK 下，于 D 盘独立目录执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --management-mode
dotnet publish src/Ipc.Desktop -c Release -r win-x64 --self-contained true -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.win-x64.lock.json -p:RestoreLockedMode=true -o .artifacts/typography-publish
$env:IPC_FROZEN_DESKTOP=(Resolve-Path .artifacts/typography-publish/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --installer-first-run-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --startup-experience
```

`--typography` 可独立检查内嵌字形、Regular/Medium真实文件、Consolas及 Microsoft YaHei UI 回退。其余本包选用 `--task-hierarchy-parameters`、`--internal-pages`、`--parameter-v3-only`、`--parameter-shell-polish`、`--visual-productization`。每次原生证据位置由 `.artifacts/WINDOWS-WPF-EVIDENCE.txt` 返回。上述为本地构建/发布资源和模拟入口核验，不执行正式安装或设备控制。

## 首次托管CI续修

PR98首次CI的Windows Server 2025实际100%缩放发现800×420未准入输入加原Unknown责任时条码越过正文视口。`f4f6045`仅减少紧凑位标题、结果框及条码标签留白，不减字号、不隐藏原责任、不改业务文本/守卫；本机余量由2.4增至27.2 DIP，受影响视觉/层级两组280项重新通过。原断言保留并增加几何诊断。首轮666项记录、后续分组最新655项记录和[原始托管失败/续修](evidence/typography-01/ci-fix/RESULT.json)分别保留来源；托管重跑的最终状态以[PR98检查](https://github.com/yuan2go/importer-pc/pull/98/checks)为准，不能替代本机Windows11实际100%/150%或现场证据。
