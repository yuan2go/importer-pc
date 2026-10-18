# PR24 R03 兼容库存闭环

本轮修复独立复审剩余 R03。代码与必要参考验证完成，更新[同一 PR24](https://github.com/yuan2go/importer-pc/pull/24)供复审，不合并 main；不代表独立复审或生产验收通过。

输入 head **3f6f762d31655ef0fe8d6562ed413beed682e2f9**；main **b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**。先提交文档治理 **4b94c3f7905dd794ce72d82e9f4ce5064865a03f**，再提交代码/测试，固定 **445e1f62bf01fe21e5f13576d2f483292571b709**。后续为文档、证据与发布记录。PR23/26及原工作区未修改。

[独立复审原报告](evidence/architecture-quality-closure-01-r03/input/independent-review.original.md)、[JSON/探针原件身份](evidence/architecture-quality-closure-01-r03/input/identity.json)保持 CHANGES_REQUIRED；[治理前入口](../archive/pre-pr24-r03-20261018/README.md)保留形成时状态。原审查01 PDF仍未取得，以上文件不是其替代品。前轮 R01～R05 [历史结果](architecture-quality-closure-01-rereview-results.md)与所有旧候选不改写。

## 实际修改与固定源码

原问题：schema1/2先经 All 按 InstanceId 去重，含包的矛盾库存可能被主绑定遮蔽，离线读者错误返回零引用。schema3反例通过不能证明旧 schema 安全。

|入口|统一规则及源码|
|---|---|
|原始库存|[ParcelCapabilityInventory](https://github.com/yuan2go/importer-pc/blob/445e1f62bf01fe21e5f13576d2f483292571b709/src/Ipc.Application.ParcelFlow/ParcelCapabilityInventory.cs#L10)：所有支持 schema1/2/3，在任何库存去重前校验有界库存、身份唯一、角色独占、完整镜像一致、已知 kind 和精确包声明/版本/模块/实现关系；disabled库存同样检查。相同内容的重复库存也拒绝，一次合法角色镜像允许。|
|配置与离线责任|[共用配置规则](https://github.com/yuan2go/importer-pc/blob/445e1f62bf01fe21e5f13576d2f483292571b709/src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs#L8)先调原库存校验，再进行拓扑与业务规则；[离线读者](https://github.com/yuan2go/importer-pc/blob/445e1f62bf01fe21e5f13576d2f483292571b709/src/Ipc.Application.ParcelFlow/ParcelConsumers.cs#L41)以同一入口覆盖 Effective、Pending 和逐件 FrozenConfiguration，保留各业务/阶段/reader责任检查。|
|真实装配|[Factory](https://github.com/yuan2go/importer-pc/blob/445e1f62bf01fe21e5f13576d2f483292571b709/src/Ipc.Assembly/ParcelCapabilityFactory.cs#L67)在 Bound/Distinct 前复用同一格式规则，随后执行原有真实安装资格检查；格式检查不加载扩展或SDK、不要求历史包可运行。|
|回归与卸载|[定向矩阵及真实卸载](https://github.com/yuan2go/importer-pc/blob/445e1f62bf01fe21e5f13576d2f483292571b709/tests/Ipc.Application.Verification/InventoryClosure.cs#L10)，使用真实 ParcelRepository、Assembly分派读者、DirectPlcApplication、Factory、签名安装及业务管理命令。|

不新增通用 Application、业务共享状态或 Platform业务SQL；不改三条主流程、UI范围、R01回执索引和R05围栏。删除已被统一规则替代的 schema3专属/离线重复检查，reader专属检查保留。

## 必要验证

命令、退出码、原日志与公开日志hash见[checks.json](evidence/architecture-quality-closure-01-r03/checks.json)。SDK10.0.402，锁定依赖、串行构建；以下最终结果均绑定445e1f6，不把早期未提交运行当作固定源码证据。

- `--inventory-closure` PASS：schema1/2及schema3控制组 × Effective/Pending/未结Frozen，每格10个非法及3个合法案例。覆盖相同重复、隐藏包、角色镜像冲突、未绑定重复、disabled悬挂包、无包模块、版本矛盾、非法包身份、未知kind；每次逐字节核对DB/config不变。合法无库存、完整镜像与精确包引用继续正确返回；三种schema均执行真实DirectPlc单次主链和配置验证/CAS拒绝。
- `--review-closure` PASS：R01～R05定向回归，含原回执身份恢复和维护期间发送围栏。
- `--quality-extensions` PASS：新精确签名样例1.6.10，真实安装中断/恢复、配置绑定/激活、单次主链、混合Supply/Pending备份责任及清偿后卸载。另用六个独立store验证schema1/2三种消费者拒绝卸载，包仍Installed、原DB/config不变；非法消费者、登记和已安装包继续保留。
- 锁定测试工程构建、Sportmaster真实Assembly Debug发布、旧Product构建均PASS，无编译警告/错误；精确冻结checker验包PASS。

修复前产品加新探针产生 **29项断言失败、退出134**，原日志、原DB/config及原独立审查保留。修复后未删关键断言、跳过非法配置或清理消费者换取通过；未进行无差别全量测试。

样例 [1.6.10身份](evidence/architecture-quality-closure-01-r03/signed-sample.identity.json)只用于本轮参考验证；包SHA256 `a9bee3992dadc35188fe409e6a920c9186419878e65e0d56c83759ea13741d15`，manifest `4ef4187258537c639b5c1f3b0210a5a941e6a999d8f16708955ca4b88b2e0b2e`。宿主源码445e1f6；插件源码最后相关变更仍b82b8d07a39fe8d2a875a05aec0cebfa4aeb94ab。独立测试目录复用精确checker合同字节，原测试输出未覆盖，见[装载字节身份](evidence/architecture-quality-closure-01-r03/signed-tests-identity.json)。不提交私钥、运行trust或二进制。

## 兼容、恢复与未验证边界

Parcel DB4/SQL1～4、Supply DB5、公共配置schema1/2/3与Application.Contracts1.6.0不变；Platform/Contracts/config/scripts/examples源码与输入head字节一致，锁文件未升级。没有数据库迁移或扩展协议升级。旧合法配置继续读取；原先被去重掩盖的非法库存会明确阻止配置采用及卸载，不自动修正历史FrozenConfiguration或其hash。保留匹配旧发布、原包和一致备份，通过有权限的责任清偿与受控迁移处理；不得编辑原件、删除消费者或重发未知请求来绕过。

本轮没有重打整个产品。7850fcc候选及1.6.8/1.6.9包保持原身份，不能作为445e1f6交付资格；下一候选必须从经复审源码新建并重新核包/运行。历史资料工具仍FAIL 267项，按(code,path)集合核差见[资料比较](evidence/architecture-quality-closure-01-r03/archive-error-comparison.json)，工具和旧失败不改写。

Windows实机安装、WPF交互/多窗/UAC/ACL、真实断电和生产长跑 **NOT_RUN**；真实SDK、设备、WCS/PLC合同、联锁和现场 **BLOCKED**；原审查01 PDF **BLOCKED_NOT_OBTAINED**。本轮不重复完整旧Supply/Inspection或已批准延期的DirectPlc UI验收。沿用[Windows与现场清单](validation/architecture-quality-closure-01-windows.md)，追加步骤：在精确新候选上导入schema1/2三类保留消费者，校验非法库存阻卸载、包/原件不变；合法包责任清偿后方可卸载；重启后再次核同一结果。Production不据参考验证自动Ready。
