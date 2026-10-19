# Sportmaster 商业交付增量结果

可执行核心工程完成；生产资格 NOT_APPROVED。设计/工作包先提交 `7b631b0`，起点 main `939462b`；通过 `ef859eb` 正常合入 PR55 的 `f3059d4`，保留旧回执和无关未跟踪表格。PR：[#56](https://github.com/yuan2go/importer-pc/pull/56)。最终实际运行源码 **`e45e9871b2ad3b85796ae74b4ac3e716996d4554`**；文档回执和合并提交不是介质源码号。

## 实现与逐项结果

|工作项|结果/证据|
|---|---|
|SCD-00|设计先行、INDEX/README 归一，原件保留；COMM/D/RV 不倒写|
|SCD-01/02|原生首配/修复五任务区、未保存输入保护、目录重新读取与写入围栏、失败不沿旧实例、检查失效、保持原契约。维护保存区分暂停/认证/外呼排空，守卫不放宽|
|SCD-03|离线任务帮助/搜索/无匹配态，支持预览契约沿用；同版完整运维/维护窗口/分诊/升级卸载/迁移/签收手册已交付，未编造联系人或 SLA|
|SCD-04|唯一统一发行器新增 Sportmaster 双目标 profile；库存、ZIP、业务/ABI、共享 DLL 一致；政策拒绝性测试5组 PASS，生产签名实际核验机制及缺证书拒绝|
|SCD-05|Windows PR 自动检查已实际执行；旧 CI FAIL 保留并修复；最终 head 必须在 PR 检查通过后 expected-head 合并。托管 main 当前未保护/规则集为空，未擅改服务端设置|
|SCD-06|可执行的同版非破坏性原生验收编排、逐项回执和完整管理员生命周期后验手册；四项管理员/双账户/重启全旅程 NOT_RUN|
|SCD-07|最终实际 DLL 核心（含确定性在途拒绝/输入保全/排空后成功）、24项原生WPF、8项卸载内核 PASS；同版脚本/夹具，验证前后介质不变，关键截图已读回检查|
|SCD-08|逐项结果/manifest/历史对象保全、精确源码/介质/运行说明；最终远端合并号和 head 以服务器回执与 PR checks 核验|

实现路径见[实现说明](../implementation/sportmaster-commercial-delivery-01.md)，原始回执及失败见[证据入口](evidence/sportmaster-commercial-delivery-01/README.md)。业务1.4.0 / Application.Contracts1.10.0 / DB8，未改变数据库或原请求语义。Sportmaster 不加 PLC/图片，DirectPlc 完整 UI 延期；登录按组合可选、指定账户非提权。

## 精确介质与运行

|目标|文件|字节|ZIP SHA256|
|---|---|---|---|
|portable|sportmaster-portable-e45e9871b2ad.zip|18505877|`298333C995042685AB8AA6FA3B283529D3F5A55F46DFF0ECC1F05FB8F334075C`|
|win-x64|sportmaster-win-x64-e45e9871b2ad.zip|104290033|`FE5A9D847E281A057BDAE9503C83FB6F667459C2B54590E98D81DAA46B25E96C`|

Windows 解压位置：`D:/ipc-scd-release-05/sportmaster-win-x64-e45e9871b2ad`。运行 `Setup.cmd`，审阅已有 Windows 账户、程序/数据根及保护 helper 后安装；指定账户普通令牌从已安装快捷方式启动。首配失败使用 `Maintenance.cmd` 或已安装的启动修复快捷方式；系统修复/卸载从 Windows“已安装应用”进入本产品维护，或使用匹配安装的 `desktop/Ipc.Desktop.exe --product-maintenance`。便携开发运行要求同版 SDK/runtime，不能替代安装授权。

复现：检出上述完整冻结 SHA，用 SDK10.0.402，运行 `python scripts/release-windows.py --profile sportmaster --source <完整SHA> --output <全新目录> --dotnet <SDK>`，再执行 `./scripts/verify-sportmaster-delivery.ps1 -Release <发行目录> -Output <全新证据目录> -Dotnet <SDK>`。脚本验证 UTF-8 结构化裁决，不将崩溃当预期拒绝。完整操作见随包[交付手册](sportmaster-commercial-delivery-01-operations.md)。

## 未验与阻塞

NOT_RUN：真正提升安装/UAC/受保护 ACL/登记/系统项/第二账户、完整升级修复卸载中断重入、真实重启删除、第二机器、目标 DPI/触控/读屏/现场负载。当前 Windows11 22631 非提权会话仅完成明确局部测试，不能替代这些旅程。已有安装内核与目录权限机制继承，现场仍须逐项证明。

BLOCKED：真实 SDK/版本化合同/设备稳定身份/实体许可、Production 精确组合资格、跨机秘密与源退役/编号责任、可信生产分发证书和授权负责方放行。实际签名成功路径无证书未执行，当前未签名、缺批准证书门禁拒绝。支持联系人、服务承诺和备份/恢复数值目标待真实合同；不设置生产默认。历史 D/RV 保持原状态。

首轮 WPF 时序失败、CI 在途守卫失败、代码页和裁决解析风险及修正均留证。工程候选 PASS 只覆盖明确执行项；合并、构建、安装成功均不构成现场商业投产批准。
