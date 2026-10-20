# 菜鸟供包台名称与图标

2026-10-20 用户指定软件名称为“菜鸟供包台”（以最新更正的“台”为准），并提供蓝底白字图片；随后明确允许将图片及程序图标提交当前公开仓库。开工 main 为 `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`。独立分支 `codex/cainiao-product-brand`；开工并行 PR62 工作台精简与 PR63 海康驱动未纳入本分支。

本次只更新产品品牌显示：Windows EXE 的 Product/Title 和图标、Sportmaster 工作台左上角与标题、兼容 Supply 工作台、其他既有窗口及离线帮助名称、安装应用显示名称和开始菜单品牌目录。内部程序集名、Application ID、数据目录、部署注册键及凭据保护用途字符串保持原身份，避免破坏既有配置和恢复；发布者仍保留工程真实来源。卸载按当前精确发行身份清理新旧品牌目录中的对应快捷方式。

原图身份、生成命令与公开许可范围见[图片说明](../../src/Ipc.Desktop/Assets/README.md)。现行显示要求取代本次修改入口中的旧 Importer PC 品牌；历史报告、原 HTML/CSS/JS、交接及原验收记录保持原身份。UI 继承 SS-UI-VIS-001 v1.1 信息层级与现行 WPF 结构，没有扩展业务或设备能力。使用项目 Impeccable Operate/WPF、polish 与 craft floor，独立 WPF 查询确认嵌入 pack URI 方式，不用网页检测代替原生检查。

构建：`D:/tools/dotnet/dotnet.exe build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -m:1 -nr:false`。

原生合成验证：`D:/tools/dotnet/dotnet.exe run --project tests/Ipc.Desktop.UiVerification -p:Application=Sportmaster -- . --visual-productization`。发行沿既有 `scripts/package-application.py --application sportmaster --target win-x64`；安装仍从完整介质 `Setup.cmd` 进入，日常运行仍用指定 Windows 账户和安装后的入口。

本次验证结果将在完成后补充。Windows 安装/UAC、真实 SDK、设备、RCS 和现场验收不属于此次品牌变更的通过结论；原 D/RV 状态保持。
