# PARAM-01 实施与验证

状态：实施中。基线与裁决见 [设计](../design/parameter-refactor-01.md)。本页在代码完成后记录真实结果，不借用旧 PASS。

Windows 原生显示/动态交互、合成 SDK/API、真实设备/RCS、目标工控机与现场分别验证；未执行项保持未执行。


## ba80 合并快照的源码对照（2026-10-20）

仅静态读取，不新增构建/测试/Windows验证。PR71冻结 `5abe82071471d525686352c094dbb79dc7fea3ce` 已随 PR69/70/72 合入main `ba80d0ea90d86b24f9d9eebd20d075da34320972`；发布范围及原产物见[快照](publication-parameter-snapshot-20261020.md)和[发布回执](publication-pr69-pr71-20261020.md)。

| 已有代码 | 当前可确认范围 | 继续完成 |
|---|---|---|
| `ParameterEditorModel` / `ParameterSettingsControl` | 四分类、真实描述参数、字段/对象错误、定位、基础输入与本地样例。 | 按V2重排唯一编辑归属及对象布局，完善状态/差异/键盘与尺寸体验。 |
| `StartupConfigurationDraft`、启动入口 | 首配与日常共模、缺配可编辑与原责任保护路径。 | 五步独立首配、未完成续办、故障维护返回及正常页面感知。 |
| `BarcodeFiltering` 及准入联动 | 完整候选、受限整串正则、唯一选中、歧义拒绝、旧null兼容与证据保留。 | 原生展示与必要风险验证；已有规则清空不得退回旧兼容。 |
| 桌面/应用 `ConfigurationSubmission` | 规范化候选、配置账及原所有者核对；Unknown/PendingRestart/NotSaved/Conflict。 | 准确呈现结果、保留更新后的输入、冲突/未知的可执行下一步及最终验证。 |
| `RcsConfigurationValidation` 与既有心跳 | 保存前静态联合校验；已有周期、编码、生命周期和HTTP证据。 | 保留两类编码的用途与历史，按V2迁移取格口字段；不把心跳传输当应用确认。 |

三组相机任务展示、waveNo/响应quantity的兼容传递、完整对象维护与V2视觉尚有工作；taskId/skuId原有解析/应用传递继续复用。V2目标见[现行裁决](../design/sportmaster-task-redesign-01.md)，不以临时参数摆放反向覆盖用户字段归属。单窗口安装和已发布原介质继续保留各自SourceCommit，此参数快照及最终合并树未据此取得新安装或现场PASS。
