# PR29 e0f5303独立复审

固定head e0f5303ff80c48f1a7ecf6964916cb21c13a7327；main baac260214f910dca9ae4628ba6449b25188e2ac；新发行源码5e3282b02616d905a628a6cbeeb9a7fbe4228a79。并行PR27/30/31/32不纳入。原仓库、候选与证据未改；临时构建及变异仅在本审查外部副本，未评论GitHub、未提交或合并。

## Standards

无确认新增规范硬缺陷。原P2在源码/实际发行DLL静态层闭环：ParcelCapabilityFactory.cs237-240共享方法传入实际业务程序集；安装248及恢复/卸载277都调用该方法。实际Windows app/desktop业务/平台/宿主DLL一致，IL探针验证两个真实入口通过。

回归有效性有层次：恢复共享helper三参调用，VC01在VersionCompatibility.cs57失败；仅把ManageAsync调用点恢复原三参直调，VC01仍全部PASS，但已提交IL探针拒绝该变异。因此P2闭环依据必须包含实际DLL的IL检查，VC01单独不能保证接线不回归。见standards/summary.json、mutant-vc01.log、helper-mutant-vc01-red.log、mutant-il.log、fixed-il.json。本次未变异head外部副本独立build与VC01也通过，源码402文件核对一致，见fixed-source-check.json、fixed-build.log、fixed-vc01.log。

## Spec

原P1配置自洽改标已闭：同一组四项相关新回归装载旧d954ba6实现失败、装载当前实现通过；原配置/候选/输入/snapshot关联检查有效。primary/p1-regression-mutation.json提供逐项结果；当前全38项Python通过。

剩余1项P2：执行方式和验证边界声明仍可被改标而获重验PASS。scripts/application_compatibility.py72-75的run_input不绑定Command/ExternalPeerProcess/资格字段，100-113仅核对OptionalFeatures/AttachedUpload与作业结果。违反docs/design/version-compatibility-01.md19的实际命令、场景、检查项及21/27的参考/Windows/真实设备边界声明。

最小反例：将真实BeforeMain记录ExternalPeerProcess从false改为true，并在Command增加未执行的--peer-fixture；候选、配置、RunId、输入事件及所有events原字节/SHA均不变，官方CLI仍exit0/PASS。原snapshot和冻结WCS/PLC/upload绑定仅有本地peer-root、无endpoint；ReferencePeer.cs9-10、19-22、58-64表明该运行走本地持久peer，不能认作独立接收器进程。不是PublicProjection允许的仅路径投影。见spec/relabelled-peer-process-cli-receipt.json、peer-process-counterexample-facts.json和SPEC-REVIEW.md。

同根边界反例：仅把Windows/RealEquipment改为PASS、Assurance改为PRODUCTION_ACCEPTANCE，保持全部事件不变，重验也接受。primary/residual-metadata-counterexamples.json记录API与CLI结果。这会错误认可记录中的验证强度/边界声明，不代表运行时Production准入或设备权限被绕过；也不表示仓库原记录已经失真。固定Git索引的原RecordSha能发现这些改写，但verify_record/CLI并未消费该锚，且应独立拒绝与自身冻结事实矛盾的声明。建议把实际执行方式纳入输入绑定并与有效配置/进程证据对照，对portable参考记录强制验证资格边界。

## 发行绑定和本次运行

直接读取两个实际ZIP，核对ZIP SHA、完整文件集合、SHA256SUMS、每个release Files全覆盖、ASSEMBLY与composition.lock；从ZIP中提取实际业务DLL读取PE元数据，版本1.2.1.0、InformationalVersion 1.2.1+5e3282b完整SHA和内嵌声明均匹配。既有三条schema2记录/原始事件/证据目录hash匹配。primary/bytes-and-evidence.json为完整结果。

新解压同一portable ZIP重新执行默认、BeforeMain、Attached三条进程/重启旅程并重验，全部PASS；primary/fresh-journeys.json列实际新RunId。记录的版本、提供者、有效配置及作业结果与实际参考运行闭合，残余P2范围是执行方式/验证资格元数据。

## 边界

Windows实际启动、交互、UAC/ACL、安装恢复卸载NOT_RUN；真实SDK/WCS/PLC/认证/实体/现场BLOCKED。本次没有重新发布候选、重跑新宿主签名扩展激活或全量C#历史回归；不能把c4e1319旧签名PASS套到5e3282b新字节。自带SHA不代替可信发布渠道或独立索引。无真实设备动作，不授予Production/现场资格。

规范轴0项新增缺陷；需求轴1项剩余P2。原P1/P2具体修复成立，但证据声明的完整一致性尚未全闭环。
