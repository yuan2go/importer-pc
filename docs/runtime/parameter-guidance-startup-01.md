# 参数用途、默认值与延后连接配置 · PARAMETER-GUIDANCE-STARTUP-01

2026-10-25，固定main `0503b967`。工作包见[七项要求与边界](../work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md)，原指引字节见[归档](../archive/pre-parameter-guidance-startup-01/README.md)，原生图与日志见[证据](evidence/parameter-guidance-startup-01/README.md)。业务版本SM 1.12.3，Application.Contracts 1.18.0；原版本、D批准、RV、历史介质和原请求身份不倒写。

## 实际行为

| 用户问题 | 当前实现 |
|---|---|
| 格口请求用在哪里 | 说明相机扫码选出有效条码后，向RCS申请该包裹的分拣格口。请求数量不是扫描累计或响应数量；指定格口只是发给RCS的请求条件，最终仍以回包为准。 |
| 参数说明只输出名字和技术键 | 说明区改为用途、填写场景、后果及范围；正常说明不再输出配置路径。错误原文及路径仅在诊断中使用。没有内容的说明折叠不显示。 |
| 处理参数含混 | 改为“接收与等待限制”，字段为“扫码消息缓冲上限”“外部服务等待时间”“待处理作业上限”，说明积压拒收、结果不明核对及容量达到上限的处理。 |
| 身份名称与重复填写 | zone-id显示“仓ID”，project-id显示“生产线ID”。只有一个“供包台编号”输入，明确写入当前候选StationCode及左右取格口站码；旧不一致值先显示并要求确认，不修改Effective、原请求或原报文。左右相机编码仍各守真实用途。 |
| 默认保留 | 新站日志及已结数据默认7天；旧安装器尚未完成的首配也只在编辑候选补入。已有运行配置的空值和显式天数不自动迁移。历史过期仍通过受控维护、备份和责任保留执行，不在运行内批量删除。 |
| 默认打开方式 | 系统→显示偏好→窗口/最大化（全屏）→保存打开方式。仅当前Windows用户，下次打开作业窗口生效；两个宿主均消费。全屏无边框、不可拖动改变大小，F11切换，Esc返回窗口；没有切换设备或业务状态。 |
| 相机/RCS后配置 | 各自页面勾选“稍后配置此连接”，新站默认勾选。明确保存DeferredConnections，保留未填或无效原输入；启动时使用只报告不可用的待配置端口，不创建对应socket/SDK或模拟成功。允许打开软件，Application阻断恢复新入场、新作业及继续发送；填写完毕取消勾选、保存并受控重启，重新执行原结构/SDK/生产资格检查。重连不能应用Pending或启用待配置连接。 |

本轮DeferredConnections为可选init属性，原ParcelConfiguration构造签名保留，null时不写JSON；旧配置/原请求字节不因新字段自动变化。只有当前Sportmaster左右读码设备ID及RCS实例可声明延后，未知或重复身份拒绝。精确包、签名、拓扑和原责任检查继续执行；延后只使未启动能力的参数与SDK前置条件可以后补。活动模块依赖闭包仍完整核验。

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
