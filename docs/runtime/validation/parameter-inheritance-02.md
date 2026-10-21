# PARAM-01 / PARAM-UX-02 参数继承增量交付

2026-10-21；PR [#83](https://github.com/yuan2go/importer-pc/pull/83)。开工main `20251b4c10db76a37f4d204332e3e6cf68340429`；继承PR80，随后整合main `3b883fe`（PR81最终改绑通知）和PR82 `fb2bba4`。独立分支/工作树开发，未改中文任务工作副本。编前设计整理提交 `cac50d1`，原件保存在[编前归档](../../archive/pre-parameter-inheritance-02/README.md)。

## 实际完成

[93项迁移表](../../design/parameter-legacy-migration-02.csv)按旧读取位置、证据、语义、归属、Application、消费者、校验/单位、生效和结论逐行裁决：已有复用/能力支持17项、本期实现/完善10项、业务专用38项、缺证语义/算法21项、废弃通用开关或实体替代7项。旧读取工程未取得，旧字段依据仍是用户盘点，未声称旧现场迁移完成。

硬件读码/称重/PLC/采图归驱动，调度/登录/上传归组件；目录、搜索、错误和差异审阅共用职责规则，首配与日常共模。Sportmaster固定左右独立Reader和共享RCS，不新增无关入口。完成有界整串允许/排除、候选原因与本地测试；作业登录与维护认证独立；日志保留只清理诊断白名单；显示名称/目标效率有真实工作台消费者并走独立偏好及草稿保护；适用称重业务拒绝越界且不伪造默认测量。保留原完整候选、无差异保存保护、敏感引用、CAS/Unknown/Pending/Effective和原请求责任。操作见[运行说明](../parameter-inheritance-02.md)。

## 源码、介质及截图身份

- 本机冻结产品源码：`13663334b2a80f8b9d4e35968ee915851474eae4`；Sportmaster **1.8.0**、共享合同**1.14.0**、DirectPlc/WcsToPlc **1.5.0**。此后提交只含验证/交付文档、原历史文档恢复和manifest，不改变产品源、配置、脚本或随包指南。
- 首轮CI检查head为上述提交；GitHub检查合并提交/介质SourceCommit为 `94a24cda27ffed71c126f92118b764c2c2b06862`。两者产品路径无差异，但媒体哈希与验证身份分别保留，不能混称同一介质。
- [本机发布清单](../evidence/parameter-inheritance-02/release/RELEASE.json)、[安装器回执](../evidence/parameter-inheritance-02/release/INSTALLER.json)、[本机原生结果](../evidence/parameter-inheritance-02/native/RESULT.json)。发布DLL/夹具均来自同一个本机SourceCommit，219条PASS、64张PNG，Windows125%及标准/紧凑/小窗几何记录在原日志。
- [完整原生日志](../evidence/parameter-inheritance-02/native/WINDOWS-WPF.log)、[条码排除](../evidence/parameter-inheritance-02/native/inherit-parameters-exclusion.png)、[记录保留](../evidence/parameter-inheritance-02/native/inherit-parameters-retention.png)、[本机显示](../evidence/parameter-inheritance-02/native/inherit-parameters-display.png)、[实际中文输入和应用](../evidence/parameter-inheritance-02/manual/display-applied.png)。截图为真实WPF，端口/业务数据为合成验证，不是现场。

| 本机交付文件 | SHA-256 |
|---|---|
| CainiaoSupplyStation_SM_Setup_v1.8.0_win-x64.exe | `4537ab01ea44fef6a381956504ebb48a69856aaf359c37d072e5dd5611923fd6` |
| sportmaster-win-x64-13663334b2a8.zip | `92e7b1aa05edba97bd4790ff4d8b58e182ecb22609bb45101db276b69d5e5aaa` |
| sportmaster-portable-13663334b2a8.zip | `aa4f4eab9c9dbc61fac2f70c3df5e47eb901c89927f275a7baa683bb563eb14e` |

本机EXE位于 `D:/deliveries/parameter-inheritance-installer-1366333/`，两个包位于 `D:/deliveries/parameter-inheritance-1366333-final/`。在线可安装交付为[CI安装介质](https://github.com/yuan2go/importer-pc/actions/runs/39557253683/artifacts/12326461214)，其原SourceCommit为94a24cda，哈希见[CI安装器回执](../evidence/parameter-inheritance-02/ci-39557253683/scd-installer/INSTALLER.json)。CI附件保留至2026-11-20；不将其链接当成永久发行。没有创建或替换正式Release。

## 必要验证与限制

| 范围 | 结果与边界 |
|---|---|
| 参数机制与真实运行消费 | 开发期72项；旧单规则字节/指纹、排除/完整性/歧义/重复/预算、左右隔离、保存冲突/Unknown/恢复/敏感引用、显示持久化/草稿、日志保留、重量范围/超时/下一件同码实际流程 |
| 配置兼容 | 61项；完整候选、旧可选配置保留、双窗口CAS、输入所有权；旧英文断言改为中文数组语法及原数量/长度要求，未删除条件 |
| 构建与发布策略 | 受影响Windows桌面零警告/零错误；策略5项、冻结构建1项、版本兼容17项；同源便携/Windows清单与安装器封装通过 |
| Windows原生 | 最终冻结DLL219项、64张截图；开发期新增参数34项、登录会话25项另保留。键盘中文输入、Tab、Ctrl+A、非法输入保留与显示消费者见[输入记录](../evidence/parameter-inheritance-02/manual/INPUT.json) |
| CI必需门禁 | [运行39557253683](https://github.com/yuan2go/importer-pc/actions/runs/39557253683)成功；同源核心/恢复、原生/视觉/参数、介质与安装器通过。[工程门禁](../evidence/parameter-inheritance-02/ci-39557253683/scd-evidence/engineering-gate.json)PASS；[生产门禁](../evidence/parameter-inheritance-02/ci-39557253683/scd-evidence/production-gate.json)按未验资格拒绝，不能解释为生产验收 |
| 本机安装启动 | 安装器版本/选项及独立路径可编辑；隔离离线维护MissingConfiguration、设备未启动、返回恢复页已核对。用户物理Esc停止Computer Use后不再操作；取消和最终退出未确认。见[启动记录](../evidence/parameter-inheritance-02/manual/STARTUP.json) |
| 未执行/缺口 | 管理员实际安装、升级/重启/双账号、安装后首启、100%物理DPI/触摸、完整IME组合、真实SDK/RCS/WCS/秤/PLC/现场；机器人/标定/远程配置等按迁移表逐项缺合同或旧读取证据，仅阻断对应能力 |

普通用户本机完整恢复专项在新数据目录授权处被既有Windows安装守卫拒绝，保留原失败；CI的合成恢复通过不能替代本机安装生命周期。所有开发期错误、修复前断言、GBK解码失败及首轮许可证缓存路径失败见[开发记录](../evidence/parameter-inheritance-02/development/RESULT.json)，没有吞异常或改门禁换PASS。

公开归档按Git原始字节核对：首次发现两份历史Codex工作包文档因Windows AUX路径合并/index问题遗漏，已按main原字节恢复；manifest校验文件名恢复原约定。修复后与开工main同为298项历史问题，新增0，[比较记录](../evidence/parameter-inheritance-02/archive/COMPARISON.json)保留修复前307项报告。未改公开范围、未上传客户原稿/SDK/凭据，不倒写原REQ/BR/CAPI/D/RV及旧1.7.0身份。
