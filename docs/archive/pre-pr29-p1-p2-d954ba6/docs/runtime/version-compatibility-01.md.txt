# 版本及兼容管理运行指南

[设计及边界](../design/version-compatibility-01.md) · [工作包](../work-packages/WP-IPC-VERSION-COMPATIBILITY-01.md)。本次运行证据另行记录，本文不把目标写成已通过。

业务版本修改在对应Ipc.Application.<业务>.csproj的Version；同时更新compositions中的同业务版本，两处不一致发布拒绝。config/compatibility/<业务>.json声明允许的精确提供者组合成员，随业务程序集内嵌；修改声明须新业务发布，普通配置不能扩大声明。设备Revision、配置Revision、ContractMajor和实现Version用途不同。

使用原package-application.py选择业务与目标。新application-release.json为schema2，带BusinessVersion、BusinessAssembly、CompatibilitySha256；ASSEMBLY.json同时带业务版本与声明hash。每个app/desktop目录的application-compatibility.json必须与程序集内嵌内容一致。原公共契约精确SHA、签名依赖及Production资格继续校验。旧候选保持原schema1和原字节，只由匹配旧程序处理。

复用smoke-application.py --package <已解压portable> --output <新证据.json>；默认、--optional-features、--optional-features --attached-upload为不同场景，需分别执行。输出记录绑定实际候选及运行配置，并在相邻*.events.jsonl保留原始进程输出。开始先写RUNNING，失败记录FAIL并保留原异常；成功才PASS，证据文件存在拒绝覆盖。Windows候选不能以portable记录替代实机运行。证据读取工具校验候选/配置/事件hash，过期或篡改不能作为同组合PASS。

启用新提供者前：发布新业务声明（若原声明未覆盖）、按目标宿主重建新不可变扩展版本、静态校验/安装、提交完整配置草稿、原件清偿与受控重启、对实际组合执行验证并保存记录。历史Unknown只核对原请求，不因版本升级重发。


## 只读身份与证据重验

```sh
dotnet /unpacked/app/Ipc.Assembly.dll --action business-identity
dotnet /unpacked/app/Ipc.Assembly.dll --action deployment-check --file /configuration/business.json --data-root /isolated/preflight
python3 scripts/application_compatibility.py --record /evidence/new-run.json --package /unpacked
# 另给实际生效配置时，必须与该次记录完全一致
python3 scripts/application_compatibility.py --record /evidence/new-run.json --package /unpacked --configuration /evidence/effective.json
```

business-identity只读编译版本和内嵌声明；deployment-check同时执行版本兼容、原精确包和语义校验，不连接设备/外呼。ProviderSets每个集合列出可共同使用的成员，选中集合内可省略业务未启用的可选能力；禁止跨集合拼接成员。新业务支持的精确参考能力包为2.0.1，示例reader为2.0.1，必须针对本次宿主公共DLL重新构建；旧1.4.0/1.0.9及PR23/24/26各自候选继续按原身份读取。

声明兼容、静态校验、运行PASS、Production资格和现场验收是不同事实。组合记录及其哈希用于身份和完整性核对，不自行证明发布者信任或现场批准；现有独立信任/资格机制继续生效。记录中未执行的组合没有PASS，不根据同版本号或同一集合推断其他组合已验证。业务版本以项目声明识别，具体交付仍用SourceCommit和文件SHA区分；不允许用手改版本号替代新发布和验证。

整合基线更新：2026-10-19 PR23/24合并至main 4afe2afacde9e2acab7648fff3440fa3cc331b6e；保留其去重前库存校验、两阶段配置激活、采集保有、恢复、容量与消费者卸载。新业务1.2.1/参考扩展2.0.1独立于前轮本地1.1.0/1.4.1和并行PR26的1.6.x，原包/记录不覆盖。

发行构建使用git archive导出的固定提交快照，编译、种子、内嵌声明及随包指南均从该快照读取；SourceRevisionId显式绑定导出提交。运行中的主工作树变化不会混入候选。先前8bdfaa9 Windows候选在构建期间发生本任务整合main，来源稳定性未获证明，保持原字节但不作为合格候选；最终候选按新快照重新生成。

最终继承main9a5d87a（PR26已合并）：复用--composition、--extension-package/--extension-trust和实际composition.lock.json，不重建选装器；保留显式--version、安装Repair回执/依赖custody及原请求只读核对。业务1.2.1与扩展2.0.1独立于前轮本地1.2.0/2.0.0。第一次快照发布因SDK排除ArtifactsPath中的源码而真实FAIL，已把source-snapshot移到独立兄弟目录，不削弱编译或契约校验。
