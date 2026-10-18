# 并发任务整合报告

工作包[WP-IPC-PARALLEL-TASK-RECONCILIATION-01](../work-packages/WP-IPC-PARALLEL-TASK-RECONCILIATION-01.md)。开工2026-10-19 01:08:15 +08:00，main **713f23d7c39743762adb884fdffa18745cad08f8**。本节为代码修改前的调查/裁决，最终SHA、验证和统计在交付时补入；当前不宣称整合完成。

## 成果保护与调查证据

[起始清单](evidence/parallel-task-reconciliation-01/start-inventory.json)保存4个Open PR（均非Draft）、完整HEAD/base/checks、26个可访问注册worktree及全部refs。GitHub main未保护、规则集空、无必需检查；合并前重新核验。所有分支沿共同基线追溯，并非只看最近提交。源任务会话实际读取，不以标题推导原要求。6个相关任务已通过受支持消息接口确认停止写入；整合在独立工作树进行，原树不清理、不reset、不强推。

主树198项未跟踪文件及PR27树35项暂存证据已在Git外保存原字节、SHA和binary patches，所有refs已bundle。PR27在盘点窗口推送a4ecfd1，原e86d664和新增证据均纳入；35项已由作者提交，不能重复认作本任务提交。忽略目录已盘点，文本报告/日志另有快照，原二进制/测试库保留原位置。原始客户HTML/PRD及受限基线不公开；两份delivery12副本核对现行工作包后按历史来源登记，不冒充新增要求。无法访问或已删除的外部会话无法取回；架构审查01原PDF继续缺失，不用02代替。

## 任务对照

|任务/原要求（实际会话）|基线、分支、PR与实际成果|证据与缺口|重复、依赖与处理|
|---|---|---|---|
|架构质量闭环01：容量/Attached隔离、采集并发、UI目标、配置扩展、原PDF；后来独立修复DB4预迁移和schema1/2冲突|PR24 f531d3a已合并；后续PR31 da9233d合并为起始main713f23d。共享维护/原件/配置及升级守卫已实现|原审查01 PDF未取得；[PR24原审](../reviews/pr24-independent-rereview-f862c2a/README.md)、[PR31原审](../reviews/pr31-independent-rereview-f068156/README.md)及历史失败保持|继承起始main，不重做PR24/31。PR30的DB6不能绕开PR31守卫|
|审查02 N01–N07实施：配置断进程、旧Attached/缺raw、容量、CAS、选装与维护；后续两P2恢复及custody竞态|PR26已合并；新PR32 codex/parcel-recovery-p2，HEAD0aac919，4笔起始main外提交，共同基线baac260。永久ID核验、持久Query依据与确定性夹具|[PR32原审](../reviews/parallel-task-reconciliation-01/pr32-original-review.md)、[结果](parcel-recovery-p2.md)，原FAIL及发布身份保留；参考范围无已确认残余缺陷|与PR31修改相邻恢复文件；合并两者语义，不按ours/theirs整文件覆盖|
|Sportmaster第二套UI：按提供HTML转换WPF，客户端参数可选；公开衍生代码，原HTML不传；后续6项P2修复|PR27 codex/sportmaster-alternate-ui，初始e86d664，盘点末a4ecfd1；共同基线713f23d，17笔。classic/supply-station、多窗共享实例、读取失效/顺序、小客户区暂停、关闭排空|[原审](../reviews/pr27-ui-independent-review.md)、[结果](sportmaster-alternate-ui-results.md)、固定候选与原失败。Windows原生交互未验|与PR30共享VM、PR29共享打包/指南。保留RCS类型化结果和新版UI，UI不拥有业务流程|
|业务版本贯通发行物、业务/组件/驱动兼容声明与真实组合证据；后续修复P1/P2并复审|PR29 codex/version-compatibility，HEADe0f5303，共同基线baac260，11笔。业务1.2.1、release schema2、内嵌ProviderSets、快照事件/实际DLL校验、维护共用入口|[最新原审](../reviews/parallel-task-reconciliation-01/pr29-original-review.md)：仍有执行方式/验证资格改标P2；原P1配置和维护P2已修|修复剩余P2；与RCS契约1.7和新组件版本声明联动；不删兼容守卫换通过|
|按实际给定sorter.task.dest_request及三类响应实现首个Sportmaster RCS组件，明确命名|PR30 codex/sportmaster-rcs-task，HEAD822ce92，共同基线baac260，6笔。纯准备T1、HTTP原件T3、正常/NEED_UNPACK/拒绝、Unknown禁重发、签名扩展、SQL6/Contracts1.7|实际读原提示词和独立协议探针；[设计](../design/sportmaster-rcs-task.md)、[结果](sportmaster-rcs-task.md)。原审无已确认阻断；真实RCS未联调|与PR31迁移守卫、PR32持久Query依据相交；与PR29内嵌版本声明冲突，需新精确包|
|不测试发布任务：提交现有成果、输出数量并合并当时PR|PR28 e43940e已合并，原main baac260；发布8份既有产物及回执|[原回执](publication-20261019.md)保留NOT_RUN；无未提交新增源码|本次显式要求组合必要验证，旧不测试仅属原任务，不扩展为永久限制|

## 文档权威与裁决

有效顺序：本次用户边界→框架F决策及19/20→22拓扑→质量闭环/PR31/PR32恢复增量→RCS协议与版本声明→UI参数/投影。后续增量只替代其明确范围，历史报告/PDF的形成时结论保持。旧Supply Product/Storage5不成为Parcel操作入口。根README中错指reviews/runtime的链接修复为docs路径。原入口字节在[归档](../archive/pre-parallel-task-reconciliation-01/README.md)。

|冲突|来源与影响|采用方案与理由|
|---|---|---|
|C01 原件恢复及DB迁移|PR31先判旧未结；PR30追加SQL6；PR32永久ID/持久Query。自动文本合并也可能先升级破坏旧读者|保留迁移前判定，SQL1–5原样，SQL6只追加；DB5→6有未决责任同样保守判定，专用custody-only仍显式走5。持久原件/永久ID是依据，64条投影不替代|
|C02 兼容声明与RCS|PR29仅列参考ProviderSets、Contracts1.6；PR30组件1.0.1针对旧精确宿主。直接组合会拒绝RCS或装错包|按最终真实Contracts1.7/业务版本更新声明，新增RCS允许组合；旧包字节不覆盖，用新不可变版本针对最终宿主构建验证|
|C03 证据改标|PR29最新独立复审反例，可将本地peer说成外部进程/Windows PASS|扩展既有输入事件绑定和重验，核对命令、实际peer配置/场景及参考资格；保留schema1/2历史原件，不伪造旧事件|
|C04 打包/多UI/文档|PR27新增UI随包说明、PR29 schema2及稳定快照、PR30 RCS指南/包选装，共改脚本与compositions|合并同一发布入口，锁定最终版本、契约、组件和全部必要指南；没有第二打包系统|
|C05 VM与恢复投影|PR27共享目标/读取顺序，PR30 UnpackRequired与完整结果，PR32Query接受依据|保留全部投影和防串位/防过期结果的守卫；业务判断仍在Application，UI仅调用契约|
|C06 多份当前设计/历史OPEN|源分支各自INDEX与manifest、框架说明互相覆盖，旧报告自称当前|当前入口统一索引；历史原字节、PDF、失败、候选与SHA保留；manifest只按最终受控文件集合重算|

## 产物分类与缺口

保留所有4个PR代码、配置、脚本、工作包、设计、验证记录和历史快照；PR29/32此前仅在Git外的独立报告在本次原字节归档。源候选/签名包/数据库保持原位和指纹，当前公开源代码与合成证据不夹带私钥、SDK、客户原稿或机器配置。02 PDF SHA256 dce6b0e235c44e414cc14a976dbd3cea0d24f634aab1276be894b6b69b62283d，另两份已公开PDF及同源UI资产继续原字节。陈旧远端docs/ui-assets和docs/final-ui-assets分支作为历史候选保留：非Open PR，不为祖先关系盲合旧入口。

## 最终验证与交付

待组合实现和验证后，在此登记固定源码、要求—位置—来源—结果、每PR实际合并身份及统计截止点。源码核实、自动化、参考进程、Windows实机和真实设备现场分别列明；本次调查不是现场PASS。
