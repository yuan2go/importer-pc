# 版本管理补齐 · 交付结果

用户2026-10-19授权，WP-IPC-VERSION-COMPATIBILITY-01。开工main b2dc55b；执行中依次继承PR23/24的main4afe2af、PR26的main9a5d87a，最终继承仅文档PR28的main baac260214f910dca9ae4628ba6449b25188e2ac。并行PR27属于UI，未纳入本包。不改原工作区未跟踪资料或其他工作树。

## 实际变化

- 三个当前Application版本 **1.2.1** 从业务项目贯通编译程序集、application-release.json schema2、ASSEMBLY.json、composition.lock.json和打包回执。发布还核对实际编译身份，commit/文件hash仍保留。
- 业务内嵌自己的精确ProviderSets声明。所选设备与服务成员必须共同落入一个集合；错业务、未声明实现/版本/合同、跨集合混搭拒绝。外部JSON连同manifest一起修改也不能扩大内嵌声明。
- 配置校验与构造复用原工厂、原库存/签名/依赖/契约指纹及业务语义校验；原件和迁移屏障不变。业务公共契约继承main **1.6.0**，未新增业务接口、SQL或配置schema。
- 接入既有选装发行脚本；保留PR26显式--version和安装/Repair/依赖custody、composition.lock及原请求查询设施。新参考扩展 **2.0.1** 对目标Release公共DLL重新构建，不覆盖旧包。
- 发行从固定Git提交快照读取源码、配置、声明和指南。组合记录接入原smoke入口，保存唯一RunId、RUNNING/PASS/FAIL、实际Effective配置、提供者、候选/发行/声明/事件SHA；拒绝覆盖旧运行文件，重验拒绝错候选、错配置或改写事件。

Supply/Inspection保留其原ProductManifest/组合及产品路径；不把历史业务改造成新扩展平台。业务版本、公共契约版本、包版本、配置修订和数据库版本分开管理。声明本身不代表某个组合已测试或已取得现场资格。

## 固定源码及验证

实际运行/配置/发布闭包 **c4e131968964458467c3a03eddf90832ef85b309**。之后只有AA22备份测试夹具、main文档PR28整合、报告/证据/manifest及PR回执；候选保持原SourceCommit。所有源码/包/运行身份见[证据入口](evidence/version-compatibility-01/README.md)、[检查](evidence/version-compatibility-01/checks.json)、[组合索引](evidence/version-compatibility-01/combination-index.json)和[候选](evidence/version-compatibility-01/candidates.json)。

已通过：核心构建；版本拒绝与真实配置草稿保护、AC19能力、AA22拓扑/恢复/资源、采集生命周期、去重前库存、两阶段配置确认；Python版本/记录、指南和已有选装包测试。四个实际候选完成，三个portable独立解压进程主链、前置及Attached上传的原件/重启/原查询/单次执行旅程通过。三种原WPF组合兼容构建通过。

签名参考能力包2.0.1针对实际解压的WcsToPlc Release公共DLL构建；独立验证进程加载同一Release业务、平台、驱动/组件字节（验证器自身DLL单列hash），实际完成认证、同件重量、原图上传、WCS、PLC及原查询和停止。不是用Debug包冒充Release。真实启动的未声明9.0.0提供者拒绝，失败记录保持FAIL，索引另标“拒绝符合预期”。

初始失败均保留：SDK把ArtifactsPath下源码排除导致发布编译失败，修为独立兄弟快照目录；发布洁净检查拒绝自行生成的锁文件，使用已有准确锁名称；系统Python无Pillow，使用既有依赖运行时。AA22即时备份与后台健康调用存在排空时序竞争：诊断记录返回Rejected，基线及诊断调度可通过；仅测试改为等待公开维护可用性，对明确在I/O之前的本地拒绝有界重试，原备份/恢复断言和运行守卫不变。未删断言、跳过关键验证或吞异常换PASS。

## 运行与边界

沿[运行指南](version-compatibility-01.md)打包/身份查询/部署校验/执行和重验。示例：

```sh
python3 scripts/package-application.py --application wcs-to-plc --target portable --output /new/candidate --dotnet /pinned/dotnet
python3 scripts/smoke-application.py --package /new/unpacked --dotnet /pinned/dotnet --output /new/evidence/run.json
python3 scripts/application_compatibility.py --record /new/evidence/run.json --package /new/unpacked
```

**NOT_RUN**：Windows实际启动、交互、截图、UAC/ACL、设备安装及现场操作。**BLOCKED**：真实SDK/认证/WCS/PLC合同、实体关联/复位/联锁和现场验收。Production继续NotReady，D/RV和历史报告/候选不倒写。旧1.1.0及1.2.0本地候选/包按原身份读取，不能替代最终c4e1319结果。

公开范围仅获授权通用实现和合成证据；不上传ZIP/ipcx、私钥、SDK、字体、生产数据或客户材料。资料检查与业务检查分开，原265项PPTX媒体类型和2项UI元数据失败保持，新增交付错误须为0。

公开组合JSON仅对Command本机路径作显式PublicProjection，原件SHA和公开副本SHA分别记录，原始字节本地保有；实际配置、原事件和身份hash不变。资料首次新增本机路径标记和checksum文件名错误已修正，不修改历史2项UI元数据或265项PPTX失败。

交付[PR #29](https://github.com/yuan2go/importer-pc/pull/29)已创建并读回OPEN/MERGEABLE，创建时head1570b32；目标main，未合并。本回执不改变运行闭包c4e1319和原候选。
