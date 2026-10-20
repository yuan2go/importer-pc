# 菜鸟供包台名称与图标

2026-10-20 用户指定软件名称为“菜鸟供包台”（以最新更正的“台”为准），并提供蓝底白字图片；随后明确允许将图片及程序图标提交当前公开仓库。开工 main 为 `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`。独立分支 `codex/cainiao-product-brand`；开工并行 PR62 工作台精简与 PR63 海康驱动未纳入本分支。

本次只更新产品品牌显示：Windows EXE 的 Product/Title 和图标、Sportmaster 工作台左上角与标题、兼容 Supply 工作台、其他既有窗口及离线帮助名称、安装应用显示名称和开始菜单品牌目录。内部程序集名、Application ID、数据目录、部署注册键及凭据保护用途字符串保持原身份，避免破坏既有配置和恢复；发布者仍保留工程真实来源。卸载按当前精确发行身份清理新旧品牌目录中的对应快捷方式。

原图身份、生成命令与公开许可范围见[图片说明](../../src/Ipc.Desktop/Assets/README.md)。现行显示要求取代本次修改入口中的旧 Importer PC 品牌；历史报告、原 HTML/CSS/JS、交接及原验收记录保持原身份。UI 继承 SS-UI-VIS-001 v1.1 信息层级与现行 WPF 结构，没有扩展业务或设备能力。使用项目 Impeccable Operate/WPF、polish 与 craft floor，独立 WPF 查询确认嵌入 pack URI 方式，不用网页检测代替原生检查。

构建：`D:/tools/dotnet/dotnet.exe build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -m:1 -nr:false`。

原生合成验证：`D:/tools/dotnet/dotnet.exe run --project tests/Ipc.Desktop.UiVerification -p:Application=Sportmaster -- . --visual-productization`。发行沿既有 `scripts/package-application.py --application sportmaster --target win-x64`；安装仍从完整介质 `Setup.cmd` 进入，日常运行仍用指定 Windows 账户和安装后的入口。

最终运行源码冻结为 `967ebc8daf377a99561914d57bd807017dcf2d14`。Sportmaster、Supply 构建均为 0 警告/错误；最终发布 DLL 的 38 项原生合成检查 PASS，包含首配、双位、页面/布局切换、焦点/选择/草稿、异常及 800×420 客户区。实际 EXE 的 ProductName、FileDescription 均为“菜鸟供包台”，提取图标与生成 ICO 逐像素一致；原生安装向导按精确进程/窗口标题确认后正常退出（0），未安装或配置机器政策。

本机完整介质 `D:/releases/cainiao-branding-03.zip`，SHA-256 为 `c6a31dcd1ff90979cfc767748c6bbb6ea12f5c9b8f90dcd0b878844474503579`；746 项库存哈希和 ZIP CRC 通过。解压后运行 `Setup.cmd`，继续沿原安装与身份政策；此次未向 GitHub Release 发布二进制。早期文件占用打包失败、EXE 文件说明错误及验证探针修正均保留在[证据索引](evidence/cainiao-branding-01/README.md)，最终介质未覆盖原件或使用旧 PASS。

当前图标原图来自本次用户附件；原 HTML/CSS/JS、完整交接及同源 PNG 仍是 SS-UI-VIS-001 v1.1 历史设计。本次截图来自实际 WPF/Dispatcher 的合成夹具，125% DPI，标准与最小客户区均已查看。Windows 安装/UAC、真实 SDK、设备、RCS 和现场验收未做；其他业务完整运行未验，历史 D/RV 状态保持。本次新增 manifest 项及新增 Markdown 相对引用单独核验，不宣称全量历史资料通过。
