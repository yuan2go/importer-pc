# 版本及兼容管理运行指南

[设计及边界](../design/version-compatibility-01.md) · [工作包](../work-packages/WP-IPC-VERSION-COMPATIBILITY-01.md)。本次运行证据另行记录，本文不把目标写成已通过。

业务版本修改在对应Ipc.Application.<业务>.csproj的Version；同时更新compositions中的同业务版本，两处不一致发布拒绝。config/compatibility/<业务>.json声明允许的精确提供者组合成员，随业务程序集内嵌；修改声明须新业务发布，普通配置不能扩大声明。设备Revision、配置Revision、ContractMajor和实现Version用途不同。

使用原package-application.py选择业务与目标。新application-release.json为schema2，带BusinessVersion、BusinessAssembly、CompatibilitySha256；ASSEMBLY.json同时带业务版本与声明hash。每个app/desktop目录的application-compatibility.json必须与程序集内嵌内容一致。原公共契约精确SHA、签名依赖及Production资格继续校验。旧候选保持原schema1和原字节，只由匹配旧程序处理。

复用smoke-application.py --package <已解压portable> --output <新证据.json>；默认、--optional-features、--optional-features --attached-upload为不同场景，需分别执行。输出记录绑定实际候选及运行配置，并在相邻*.events.jsonl保留原始进程输出。开始先写RUNNING，失败记录FAIL并保留原异常；成功才PASS，证据文件存在拒绝覆盖。Windows候选不能以portable记录替代实机运行。证据读取工具校验候选/配置/事件hash，过期或篡改不能作为同组合PASS。

启用新提供者前：发布新业务声明（若原声明未覆盖）、按目标宿主重建新不可变扩展版本、静态校验/安装、提交完整配置草稿、原件清偿与受控重启、对实际组合执行验证并保存记录。历史Unknown只核对原请求，不因版本升级重发。
