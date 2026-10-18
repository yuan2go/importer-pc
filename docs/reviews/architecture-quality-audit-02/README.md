# WP-IPC-ARCHITECTURE-QUALITY-AUDIT-02 · 报告发布与实施交接

[完整审查 PDF](WP-IPC-ARCHITECTURE-QUALITY-AUDIT-02.pdf) · [下一阶段 Codex 提示词](Codex-next-phase.md)

## 原报告身份

本目录按用户 2026-10-18 的后续授权发布已交付的原 PDF，并提供下一阶段实施提示。PDF 共 22 页、325136 字节，SHA-256：

`dce6b0e235c44e414cc14a976dbd3cea0d24f634aab1276be894b6b69b62283d`

审查固定 main 为 `2126ac2c06a0c352d96afbbf13ec1a719c352738`，实际运行源码为 `8981d54a3dcb45aa08ea8953f1dd9d4cd2f079f7`；两者在报告列明的运行闭包范围内无差异。审查时间和验证层级详见 PDF §02。PDF 字节不变，其中“只读、未提交”描述的是原审查时点。本次发布不代表修复完成，也不改变历史需求、设计或验收结论。原始证据 ZIP 已随原审查单独交付，本目录未把摘要当成原始运行日志。

## 最新 main 与并行变更复核

发布准备时于 2026-10-18 13:06–13:10 UTC 重新读取远端 main、工作区、现行文档、关键实现和开放 PR。main 仍为上述审查 SHA，本地受审工作树干净；本次使用独立文档工作树。因此，A01–A09 没有因审查后 main 变更而关闭；下一轮仍须在执行时复核，不机械套用历史问题。

另有 [PR #23](https://github.com/yuan2go/importer-pc/pull/23) 尚未合并，head 为 `966ec6aa17dfe29b96fc9b9113f3037ba586f6a0`。其 [工作包](https://github.com/yuan2go/importer-pc/blob/966ec6aa17dfe29b96fc9b9113f3037ba586f6a0/docs/work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md)与代码增量增加原采集的观察/作业所有者交接和保有/释放契约，将 Application.Contracts、参考能力包及组合声明升级为 1.5.0。它会影响原图释放、历史归档、公共契约和签名包兼容；未改本轮的配置激活提交、Attached 缺回执恢复、历史件数上限或新产品扩展管理入口。PR23 的测试/发布描述属于该分支自己的证据，不作为 main 已有能力或本次重跑结果。本次 PDF 发布不代为合并 PR23。

## 下一步判断与顺序

保留现有三独立 Application、按投递位执行、业务原请求冻结、SQLite 事务/Outbox、受控扩展、资源租约和恢复屏障。工业级完善的重点是连续作业、明确所有权、故障恢复及可实际操作的交付链；当前证据不支持重建通用框架或全面改名。

|工作包|当前证据与目标|依赖和必要验收|
|---|---|---|
|N01 文档先行，A09|22 继承仍有效的19/20，当前新产品操作入口却仍夹带 Supply 指南；先整理现行规范、操作指南及随包内容，保留原始要求和历史证据。|先完成，再开始业务代码；按实际产品核启动、新 Application、Driver 接入、参数修改和恢复路径。|
|N02 配置激活，A01 / P1|[激活路径](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L85-L115)在资源成功前写入候选 Effective；突然退出会丢失上一个已确认版本。|同一配置账内完成候选尝试、成功确认和中断恢复；定向验证保存后、Create 后及部分 Start 后终止重启，保留 CAS/资源清理。|
|N03 状态责任，A02 / P1、A04 / P2|[缺 raw 恢复](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L122-L161)把已完成主链统一改为 Blocked；[身份冲突](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L185-L190)只写审计。|主链、Attached 和原回执责任分别处理；主结果不倒退、原请求不盲重发；冲突在受影响位持久可见并有受控解除，另一位继续。|
|N04 容量生命周期，A03 / P1|[全历史计数](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L185-L193)达到上限后反复 Resume 仍重新暂停。|归档资格依赖 N03 的清偿语义，并核对 PR23 原采集保有；活跃资源有界、已结历史可查询、身份去重仍有效、备份恢复兼容。|
|N05 客户端交付，A05/A06 / P2|Sportmaster 页面仍取全局一个 Current；schema3 旧表单只读是22明确保护，但安装后完整维护能力未闭合。|依赖 N02/N03；双位结果归属、完整参数/实例/绑定编辑、草稿/Pending/Effective/失败可辨；页面切换共享实例，修改一项参数不丢另一位。|
|N06 扩展与发行，A07/A08 / P2|[ParcelCli](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/src/Ipc.Assembly/ParcelCli.cs#L9-L64)未接 Recover/Uninstall；[发布脚本](https://github.com/yuan2go/importer-pc/blob/2126ac2c06a0c352d96afbbf13ec1a719c352738/scripts/package-application.py#L13-L65)未贯通可选 Driver/Component 包集合与实际发行物。|复用 Store 和 compositions；实际安装中断恢复、在用/原件/备份/逆依赖卸载保护、清偿后退休，以及不同选装集合的解压、安装、绑定、启动。|
|N07 有依据的简化|页面声明、装配元数据、提供者参数和兼容锚点存在可收敛处；不应挤占确定缺陷修复。|随 N02–N06 涉及的职责调整收敛，不强制全量改名/拆包；公共 ID 和历史原件语义保持兼容；旧不可变包及身份不覆盖，公共契约变更按新版本重新构建、签名并处理原版本引用；真实合同缺口单独记录。|

N01 先完成；随后 N02 与 N03 可按职责安排，N04 基于清偿语义推进，N05/N06 在相应生命周期稳定后闭环，N07 随相关改动处理。N01–N07 沿用 PDF §11 的建议编号，不再新增一套工作包编号或验收框架；具体实现和提交拆分由执行者判断。

## 实施与验证边界

三个主流程、Sportmaster 左右独立 reader、另两种业务顺序位、同件重量、登录及取图/上传可选策略均按最新已确认设计继承。DirectPlc UI 仍按已批准延期处理；Sportmaster 不新增展图或长期图库要求。普通工程实现由 Codex 判断，真实协议、物理完成信号或用户需求缺口不能由测试、报告或参考实现反向创造。

本次仅发布报告、交接和索引/清单，不改业务代码、配置、既有测试或现行设计。发布前的 INDEX 与 manifest 原字节保留在上述固定 Git 提交中。下一阶段代码工作前应实际整合相关现行文档，历史材料明确定位并保留；不能以删除原件、改弱需求或新增“最新说明”掩盖冲突。

只复用既有必要测试、故障入口和发布冒烟；不以全库反复运行、测试数量或新校验器作为完成指标。文档链接/哈希检查与业务验证分开；Windows 实际、真实 SDK/协议及现场证据各自报告，没有执行就不标 PASS。
