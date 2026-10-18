> PR29 P1/P2整改：原head d954ba6的组合记录重验存在配置与原事件未关联缺口，Windows恢复/卸载漏传业务程序集。整改设计与执行计划见[本包](../work-packages/WP-IPC-VERSION-COMPATIBILITY-01.md)；旧候选与结果保持原身份，新验证另记，不以旧PASS替代。

# 版本及兼容管理运行指南

[设计及边界](../design/version-compatibility-01.md) · [工作包](../work-packages/WP-IPC-VERSION-COMPATIBILITY-01.md)。本次运行证据另行记录，本文不把目标写成已通过。

业务版本修改在对应Ipc.Application.<业务>.csproj的Version；同时更新compositions中的同业务版本，两处不一致发布拒绝。config/compatibility/<业务>.json声明允许的精确提供者组合成员，随业务程序集内嵌；修改声明须新业务发布，普通配置不能扩大声明。设备Revision、配置Revision、ContractMajor和实现Version用途不同。

使用原package-application.py选择业务与目标。新application-release.json为schema2，带BusinessVersion、BusinessAssembly、CompatibilitySha256；ASSEMBLY.json同时带业务版本与声明hash。每个app/desktop目录的application-compatibility.json必须与程序集内嵌内容一致。原公共契约精确SHA、签名依赖及Production资格继续校验。旧候选保持原schema1和原字节，只由匹配旧程序处理。

复用smoke-application.py --package <已解压portable> --output <新证据.json>；默认、--optional-features、--optional-features --attached-upload为不同场景，需分别执行。输出记录绑定实际候选及运行配置，并在相邻*.events.jsonl保留原始进程输出。开始先写RUNNING，失败记录FAIL并保留原异常；成功才PASS，证据文件存在拒绝覆盖。Windows候选不能以portable记录替代实机运行。证据读取工具除校验候选/配置/事件hash，还必须核对原始snapshot配置及场景/结果；d954ba6旧实现缺少此关联，不能仅因旧工具返回PASS认定重验完整。

启用新提供者前：发布新业务声明（若原声明未覆盖）、按目标宿主重建新不可变扩展版本、静态校验/安装、提交完整配置草稿、原件清偿与受控重启、对实际组合执行验证并保存记录。历史Unknown只核对原请求，不因版本升级重发。


## 只读身份与证据重验

```sh
dotnet /unpacked/app/Ipc.Assembly.dll --action business-identity
dotnet /unpacked/app/Ipc.Assembly.dll --action deployment-check --file /configuration/business.json --data-root /isolated/preflight
python3 scripts/application_compatibility.py --record /evidence/new-run.json --package /unpacked
# 另给实际生效配置时，必须与该次记录完全一致
python3 scripts/application_compatibility.py --record /evidence/new-run.json --package /unpacked --configuration /evidence/effective.json
```

business-identity只读编译版本和内嵌声明；deployment-check同时执行版本兼容、原精确包和语义校验，不连接设备/外呼。ProviderSets每个集合列出可共同使用的成员，选中集合内可省略业务未启用的可选能力；禁止跨集合拼接成员。当前业务1.2.2支持的精确参考能力包为2.1.0，示例reader为1.2.0，Sportmaster RCS为1.0.2，必须针对本次宿主公共DLL重新构建；旧1.4.0/1.0.9及PR23/24/26各自候选继续按原身份读取。

声明兼容、静态校验、运行PASS、Production资格和现场验收是不同事实。组合记录及其哈希用于身份和完整性核对，不自行证明发布者信任或现场批准；现有独立信任/资格机制继续生效。记录中未执行的组合没有PASS，不根据同版本号或同一集合推断其他组合已验证。业务版本以项目声明识别，具体交付仍用SourceCommit和文件SHA区分；不允许用手改版本号替代新发布和验证。

## 当前记录与历史兼容

新记录schema3在首条CombinationInput冻结实际命令、工作目录、候选/配置、场景、计划检查和资格边界；独立peer还必须有实际启动PID、夹具SHA、健康成功及与所有实际端点一致的事件。命令、外部进程、检查项或Windows/真实资格改标均拒绝；参考进程永远不能授予Production。

旧schema1/2保留原字节和形成时结论，其命令没有被冻结。当前重验另需 `--record-sha256 ORIGINAL_RECORD_SHA256`，该SHA必须来自独立可信发布或固定Git索引，不能从待审文件本身重新计算作为信任依据。然后仍核原snapshot、配置、结果、执行方式与参考资格；缺旧证据则保持未证明。新schema3的自带事件/hash只证明一致性，整个证据集替换仍需受信发布渠道/独立索引判断。

```sh
python3 scripts/application_compatibility.py --record /evidence/legacy-run.json --package /original-unpacked --record-sha256 ORIGINAL_TRUSTED_RECORD_SHA256
```

当前发行使用同一package-application.py的固定git archive快照；源码、内嵌声明、配置、随包指南及SourceRevisionId同源。业务1.2.2、Contracts1.7/DB6与新精确扩展分别识别，不覆盖旧1.2.1/2.0.1/1.0.1候选。PR29原历史基线和失败说明保存在[原字节](../archive/pre-parallel-task-reconciliation-01/README.md)及原结果/证据，旧PASS不表示当前组合已验。安装、恢复、卸载共用含实际业务程序集的发行校验；Windows UAC/ACL/真实维护交互NOT_RUN，真实设备/协议/现场BLOCKED。
