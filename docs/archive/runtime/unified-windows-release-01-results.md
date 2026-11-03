# 统一 Windows 发行构建与核验

## 固定来源与裁决

输入 main 为 `c445b0ec32a3db44f00a1968f3c501aa36eb786f`。发行源码固定为 `7acbb2c13d6b35d4aeff8b075b2ff52372aa2042`，可由 PR #48 追溯；报告/证据回执提交不是二进制来源。该源码包含已合并的 PR #44/#45/#46，新增的是打包流程，未改变业务、DB、安装权限或设备控制逻辑。三个 Parcel Application 为业务1.3.3、Application.Contracts1.9.0、DB7；Supply/Inspection保持兼容参考入口自身版本。

开放 PR #47 有产品化/DB8/ABI增量，本任务没有合并或移植该业务。仅复用其已经公开的冻结SourceLink、Windows tar长路径/管道处理和双宿主DLL校验修复；最终包均由本任务实际构建，没有把PR #47的包或Windows结果改标为本任务结果。PR #48为独立开发分支交付，不绕过main保护。

## 链路核查

- Setup.cmd → 原生 `--setup` → DeploymentMedia完整性 → WindowsDeploymentRequest固定配置/包hash → 独立helper/UAC → 机级账户与ACL → 并排程序/种子/扩展 → 快捷方式 → 最后部署登记。失败不代表已安装，不能用包的静态核验关闭中断/授权场景。
- 正常桌面/控制台通过原装配进入业务；ParcelAssembly、ParcelRuntime、Supply ProductRuntime、InspectionRuntime在配置/持久资源之前执行同一身份政策。指定非提权SID可运行，deny-only管理员身份允许；错误SID、真正提升和服务身份拒绝。原生政策/ACL仍须Windows验证。
- 独立维护不要求业务成功启动；按原配置账/原身份读取，保存仅产生Pending，使用原hash/CAS、应用所有权、维护审计和原件保全。实际冻结DLL已验证离线读取/校验/保存、旧hash拒绝及损坏原件保留；使用合成授权回调，不证明Windows提升流程。已确认Effective对首次修复的约束仍属于PR #47增量，未在本任务放宽。
- 扩展沿精确合同hash、签名/独立信任、依赖闭包和消费者机制，不加载模块来检查包。默认参考组合无离线扩展，不含厂商SDK/生产信任；缺真实能力时仍BLOCKED。安装成功不代表扩展激活或业务许可。

## 流程变化

`scripts/release-windows.py`固定完整SHA，在独立Git快照中调用原有发布权威，要求干净工作树及global.json精确SDK。每次新目录，逐包保留命令/完整日志/状态；逐文件库存、ZIP成员/内容/CRC、Application与合同版本、双宿主公共DLL、配置和原生入口全部复核。整批成功后才创建RELEASE.json及整批SHA256SUMS。失败或中断的半成品不能作为整批发布。

`scripts/package-application.py`将编译区移出媒介、冻结SourceLink、禁止公共DLL分叉、处理Windows长路径和git archive管道。仍由原扩展发布机制接纳精确包，不增加第二套装配权威。

`.github/workflows/unified-windows-release.yml`提供手动Windows2025构建，30天证据/媒介分开保存、只读仓库权限，无自动发布/覆盖。该工作流本轮NOT_RUN；本轮实际构建为Linux交叉构建。

## 必要验证与边界

既有扩展闭包3项、版本/证据17项、新媒介负向3项通过。另直接执行三个既有源码入口顺序断言，见static-entry-order.json；没有执行需要PowerShell的整套脚本测试，不把静态断言记为Windows身份结果。冻结DLL探针涵盖7种合成账户政策、配置离线修复及实际Windows媒介检查，完整探针源码随证据提供。

Windows原生UAC、ACL、真实账户切换、安装中断、升级/恢复与桌面交互：NOT_RUN。真实SDK/协议/设备及Production组合资格：BLOCKED。商用验收：NOT_APPROVED。校验和不代表发行者签名或外部信任；未进行独立双构建字节复现，不声称ZIP确定性。

## 失败与历史保留

第一轮本地冻结d77e28f的Sportmaster portable构建完成，但出包后目录出现source-snapshot/build-intermediates残留，独立精确库存核验拒绝；没有成功整批清单。修正为媒介外的独立临时编译区。第二轮本地SHA3d95dc0在切换到GitHub可达冻结源后取消，不作为发布。最终7acbb2c为新目录独立构建。两轮原BUILD回执及失败日志随证据保留，不倒写PASS。

原e79a8bf/16ecb57两组Release、六ZIP、十附件及publication-deployment-20261019.md不覆盖、不重签、不改来源；历史未验状态保持。

第三轮曾在WcsToPlc win-x64开始时遇到执行环境transport断开，原进程结束，BUILD保持当时BUILDING，不伪称失败/成功已落盘。增加内核锁和显式续建后，保留原attempt回执、重新校验5套原ZIP、隔离半成品，再由相同7acbb2c及相同SDK继续剩余项；编排脚本SHA与二进制源码SHA分开。

扩展实测首次合成输入为2048位测试密钥，被宿主的3072位最小强度要求拒绝；这是测试输入不合要求，门禁不作修改。另生成3072位TestOnly签名参考包，实际冻结宿主静态接纳通过；追加payload篡改被大小/完整性门禁拒绝、Production测试信任拒绝，且portable检查器与Windows目标Platform字节一致。私钥不入库、不交付；参考扩展未加载执行，测试包不装入默认媒介。

## 实际发行结果

状态：**VERIFIED_CROSS_PLATFORM，8/8**。完整身份见[RELEASE.json](evidence/unified-windows-release-01/RELEASE.json)，逐包校验见[SHA256SUMS](evidence/unified-windows-release-01/SHA256SUMS)。三业务各portable/win-x64，另Supply/Inspection portable；各包不能与其他包的DLL/配置/扩展资格混装。5个独立业务入口已实际启动/查询/停止；三个Parcel参考旅程均通过。Supply/Inspection自身项目版本详见[兼容版本](evidence/unified-windows-release-01/legacy-versions.json)。

|Application|Target|ZIP字节数|SHA256|
|---|---|---:|---|
|sportmaster|portable|18484817|`fbe41d30cf005b4585333ed744305e735714abdadbec88946abd5fe610e2f2c5`|
|sportmaster|win-x64|104352419|`2dc02b2cadbdd3ff29d23f99a87a87b4d525f4a48bc30806f74eafb579a56a70`|
|direct-plc|portable|18447751|`40a10615d0e44c0bb7518ba7c167e3980650f329f7935a98550c20537e36aab7`|
|direct-plc|win-x64|104269265|`1cddf459c900aac4bea488bcc8ac1304059196cffbd8bf799cc507b994320d67`|
|wcs-to-plc|portable|18469387|`6d3ed1c4394b3a6c3a0fcdb9ed0017d981f133411da0945eb4e77efd784ae0da`|
|wcs-to-plc|win-x64|104312501|`8bea117a2b05c97fc8aa8b3a62607803d90bb1f57145a154b4c8f64cdfc27a4d`|
|supply|portable|18482606|`702266414cea4205bcc775f9c4bbd6361d8b25d5f937d843321827d0d96a144f`|
|inspection|portable|932702|`96ecb30908e784965d24e622a725d8f15ddd7c928cf0e745365164bbb095d515`|

实际环境Linux x64、Python3.12.14、SDK10.0.402，原始SDK信息、每条命令、编排脚本SHA、续建attempt与完成时间均在BUILD/RELEASE。内核并发锁实测拒绝第二发布者且BUILD字节不变；原中断回执和日志保留。发行物为Simulation参考安装媒介，Windows三个包为自包含ZIP，各自解压后双击Setup.cmd；兼容portable包不是Windows安装器。Windows原生验收NOT_RUN、真实能力BLOCKED、CommercialAcceptance=NOT_APPROVED；未执行发行者代码签名。

## 并行合并后的时序说明

本任务构建期间，外部操作已合并PR #47/#50，并更新、合并PR #48；回读main为4f21b2d9cf6742a6775e47766c9f8d96f2804bca。未对已移动的分支强推或覆盖；本次续建机制/工作流/最终证据改以该main为基础另开补充PR，保留其DB8、产品化、打包清理guard和现行操作指南。

8套二进制固定7acbb2c，覆盖本任务开工main的PR44/45/46成果，**不包含构建过程中另行合并的PR47产品/DB8增量，不能作为4f21b2d的全成果发行或用于回读DB8**。没有将旧包改标为新main来源。后续发行须显式选取新的完整SHA；本次不追随移动分支改写既成媒介。当前main产品版本与本冻结媒介版本分开阅读。

随后main继续推进至aaa30b57cf8f810637b6502748783cdab66bbf36，新增更完整的统一发行/依赖与版本校验。本补充合并保留该活动实现，不以本任务旧编排覆盖；实际使用的续建编排源码以orchestrator-used-for-7acbb2c.py归档，其SHA256与RELEASE中的OrchestratorScriptSha256相同。旧包的校验与运行证据不因源码后来变化而改写。
