> 2026-10-27：开发阶段只支持当前契约，旧配置、凭据和数据库不再转换；保留原件并明确拒绝。当前单码规则、可选保留策略、操作员身份和管理权限守卫不变。实施与验证见 [统一契约工作包](../work-packages/WP-IPC-RCS-TIMEOUT-INDEPENDENT-REVIEW-02.md)；下文兼容转换说明保留历史适用范围。

> 2026-10-26：PR107已合入main；独立延后与心跳映射、重连及补齐配置启动的后续修复见[DEFERRED-HEARTBEAT-01](validation/deferred-heartbeat-01.md)。下文原验证仅覆盖其原提交，不能据此推断单侧延后已通过。

# 参数用途、默认值与延后连接配置 · PARAMETER-GUIDANCE-STARTUP-01

2026-10-25，固定main `0503b967`。工作包见[七项要求与边界](../work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md)，原指引字节见[归档](../archive/pre-parameter-guidance-startup-01/README.md)，原生图与日志见[证据](evidence/parameter-guidance-startup-01/README.md)。业务版本SM 1.12.3，Application.Contracts 1.18.0；原版本、D批准、RV、历史介质和原请求身份不倒写。

## 实际行为

| 用户问题 | 当前实现 |
|---|---|
| 格口请求用在哪里 | 相机扫描一个实物包裹取得一个条码，将原条码作为loadCode请求RCS分拣信息。新请求quantity固定1，移除数量配置；RCS响应数量仍按原响应保留，不强制改1。指定格口是请求条件，最终以回包为准。 |
| 参数说明只输出名字和技术键 | 说明区改为用途、填写场景、后果及范围；正常说明不再输出配置路径。错误原文及路径仅在诊断中使用。没有内容的说明折叠不显示。 |
| 处理参数含混 | 改为“接收与等待限制”，字段为“扫码消息缓冲上限”“外部服务等待时间”“待处理作业上限”，说明积压拒收、结果不明核对及容量达到上限的处理。 |
| 身份名称与重复填写 | zone-id显示“仓ID”，project-id显示“生产线ID”。只有一个“供包台编号”输入，明确写入当前候选StationCode及左右取格口站码；旧不一致值先显示并要求确认，不修改Effective、原请求或原报文。左右相机编码仍各守真实用途。 |
| 默认保留 | 新站日志及已结数据默认7天；旧安装器尚未完成的首配也只在编辑候选补入。已有运行配置的空值和显式天数不自动迁移。历史过期仍通过受控维护、备份和责任保留执行，不在运行内批量删除。 |
| 默认打开方式 | 系统→显示偏好→窗口/最大化（全屏）→保存打开方式。仅当前Windows用户，下次打开作业窗口生效；两个宿主均消费。全屏无边框、不可拖动改变大小，F11切换，Esc返回窗口；没有切换设备或业务状态。 |
| 相机/RCS后配置 | 各自页面勾选“稍后配置此连接”，新站默认勾选。明确保存DeferredConnections，保留未填或无效原输入；启动时使用只报告不可用的待配置端口，不创建对应socket/SDK或模拟成功。允许打开软件，Application阻断恢复新入场、新作业及继续发送；填写完毕取消勾选、保存并受控重启，重新执行原结构/SDK/生产资格检查。重连不能应用Pending或启用待配置连接。 |

本轮DeferredConnections为可选init属性，原ParcelConfiguration构造签名保留，null时不写JSON；旧配置/原请求字节不因新字段自动变化。只有当前Sportmaster左右读码设备ID及RCS实例可声明延后，未知或重复身份拒绝。精确包、签名、拓扑和原责任检查继续执行；延后只使未启动能力的参数与SDK前置条件可以后补。活动模块依赖闭包仍完整核验。

用户进一步确认每包固定1后，RCS参数schema与编辑候选不再提供quantity；适配器仅读取旧值验证已有冻结报文，新请求生成始终写1。旧原报文验证仍校对完整请求、参数、哈希和请求编号，不改写后重发。Sportmaster左右TCP相机首配与日常候选固定每条完整LF条码报文对应一次扫码作业，移除关联方式选择；旧仅观察配置在保存核对中显示变更，继续通过Pending和受控重启生效。通用TCP驱动的诊断模式仍供其他明确用途使用。TCP拆包/粘包按完整报文处理，同码的独立扫描保持独立；暂停、连接未就绪及原结果未知等状态仍拒绝新业务并留痕，不能宣称任何状态下所有扫码都发送成功。

PR107旧CI失败分别为：全部连接待配置时仍无条件申请机级设备租约；参数重命名后原生回归仍搜索旧“队列”文案。现按实际资源存在才申请租约，并将搜索用例改为当前“扫码消息”字段名，保留匹配、键盘和焦点断言。新端到端合成验证使用本机TCP相机对端与HTTP RCS对端，不连接现场：三次完整扫码（三者含相同条码）恰好三条原条码请求、quantity均为1；不完整TCP片段没有请求；原数量7冻结报文保持原字节。

旧仅观察/数量设置会使日常候选明确处于未保存状态，保留审阅与保存入口，不要求随便修改其他字段才能保存。旧扩展schema仍要求quantity时，新候选只能写固定1且不显示该编辑项；旧扩展当前配置数量不是1时拒绝新业务，须保存候选并受控重启，历史冻结报文仍守原身份。原生验证的个人偏好全部隔离至该次测试目录，实际用途说明与输入限制同时提供给悬浮帮助及辅助技术；过期数量字段用例替换为仍可配置的指定格口必填/条件守卫，保留实际断言。快照失败恢复用例等到有效可准入快照才注入模拟条码，避免在异步刷新中抢发。此增量本机核心与原生回归PASS，公开回执见[扫码链路验证](evidence/parameter-guidance-startup-01/confirmed-scan-flow/RESULT.json)；完整CI以PR107对应提交运行结果为准。

用户在本机卸载后指出授权服务已经移除时仍出现“管理模式不可用”会误导使用者。当前ProductMaintenanceWindow在完成卸载时清空表单和原操作入口，只显示卸载结果、实际需要的重启提示、保留数据位置及关闭；清空原计划/登记哈希并守卫重复回调。已退役登记进入同一结果终态，不重新申请管理模式。原生合成回归分别验证需重启和无需重启的回执：只有关闭按钮、没有编辑框，也没有创建认证窗口，实际验证见[卸载结果回执](evidence/parameter-guidance-startup-01/uninstall-result/RESULT.json)。本机原版本1.12.2的真实卸载另留D盘私有回执，登记已退役、专属服务已移除；原进程占用文件由Windows登记重启清理，未擅自重启，也不把这一旧版本卸载当作最新安装包的真实安装验收。

正式维护入口仅在登记仍为Installed时进入原管理模式并生成卸载计划；取消或授权失败不执行卸载，已退役状态在此之前直接返回结果。相同终态原生回归的增量回执保留在[入口守卫验证](evidence/parameter-guidance-startup-01/uninstall-result/active-entry/RESULT.json)，不改写上一条原始回执。

## 个人偏好目录

沿现有HKLM注册的D盘存储根，安装/修复时补建UserData父目录。Users只可在该目录创建自己的子目录，CREATOR OWNER继承写权限；机级配置和程序目录仍只读。既有目录ACL不自动重写，非当前用户拥有的个人目录拒绝使用，路径链接仍拒绝。当前本机原安装缺少此父目录；代码已补入安装链，本轮没有改动本机受保护根或执行UAC安装。旧安装需使用包含本次修复的安装/修复流程后，才能在原个人路径保存偏好。

原生偏好验证通过内部测试入口隔离至D盘证据目录，验证实际保存及新HWND消费；不把该结果写成受保护安装权限验收。新增真实Windows ACL断言进入既有`--clean-storage-bootstrap-elevated`及CI，本机该提权场景未执行。

## 验证与复现

先执行`. ./scripts/project-environment.ps1`。当前必要检查包括：

- `dotnet run --project tests/Ipc.Application.Verification -- . --parameter-guidance-startup`
- `dotnet run --project tests/Ipc.Application.Verification -- . --parameter-refactor`
- `dotnet run --project tests/Ipc.Application.Verification -- . --dual-camera-communication`
- `dotnet build src/Ipc.Desktop -p:BusinessModule=Sportmaster`
- 设置IPC_FROZEN_DESKTOP到同源桌面DLL后，运行`dotnet run --project tests/Ipc.Desktop.UiVerification -- . --startup-experience`。
- `dotnet run --project tests/Ipc.Desktop.UiVerification -- . --parameter-guidance-startup`；冻结介质可用`scripts/verify-commercial-wpf.ps1 -Media <介质> -Output <新证据目录> -ParameterGuidanceStartup`。

核心专项、原参数/继承回归、新原生专项与原首配/叶页回归已通过。Windows当前125%上的多客户区、键盘命令、草稿和Pending行为已验证；发布构建/冒烟身份在后续交付回执中记录。真实系统100%/目标IPC/DPI、受保护安装ACL、真实SDK/RCS、网络和现场验收保持未验。新增CI入口继承原完整门禁，不删除断言或跳过原流程。

## 固定发行回执

源码`7d7487a5`已在[PR107](https://github.com/yuan2go/importer-pc/pull/107)提交。`scripts/package-application.py`从该提交冻结Git导出，生成D盘参考win-x64介质；[交付身份](evidence/parameter-guidance-startup-01/frozen-7d7487a5/DELIVERY.json)记录ZIP及库存哈希。同源发布DLL的原生专项PASS，实际桌面EXE环境报告启动ExitCode=0；此结果不表示本机完成安装或生产业务启动。运行时变更仍为上述源码身份，后续文档提交只登记其回执。
