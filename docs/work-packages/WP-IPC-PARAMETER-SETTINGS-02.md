# WP-IPC-PARAMETER-SETTINGS-02

## 当前结论与读取顺序

2026-10-21（北京时间）重新读取远端 main，固定基线为 `42a047c3c0dbba3208e6bc6ce2910cd6ffabbf5e`；本地 HEAD 与远端一致，开工工作区干净、开放 PR 为 0。PR78 参数 V3 与 PR79 启动恢复已合并，见[发布记录](../runtime/publication-pr78-pr79-20261021.md)。**完整设计与主要原生源码已在 main，下一步为现有 PS-05 的原生交付收口及实际缺口修复。** 不再把四类参数、统一保存或“维护与迁移”入口拆分当成从零开发事项。

完成用户对实际参数页面的美学与可用性纠偏。先读 AGENTS、[文档索引](../INDEX.md)、[框架](../design/framework.md)与[F 决策](../design/framework-decisions.md)，再读 [PARAM-UX-02](../design/parameter-settings-experience-02.md)、[V3交付](../ui/parameter-settings-v3-20261020/README.md)、[交接](../ui/parameter-settings-v3-20261020/HANDOFF.md)、[原验证](../ui/parameter-settings-v3-20261020/QA.md)及实际代码。编码前先清理本任务冲突的当前指引；PR74三组作业、首配、字段合同、PR76会话修复与PR79启动回退继续复用。

V3目录中的 README、HANDOFF、CODEX-NEXT 及 SOURCE-STATE 保留形成时字节和分支语境；其中“开发分支进入 main PR”已由上述合并事实更新。当前执行提示以本工作包末尾为准。原 `bf61337`、原源码指纹、历史 PASS / NOT_RUN 及旧安装包身份不改写为本次验证。

## 设计产物已完整入库的核对结果

本次对 [V3目录](../ui/parameter-settings-v3-20261020/README.md) 与 [FILES清单](../ui/parameter-settings-v3-20261020/FILES.json) 逐项读取，并对实际文件计算 SHA-256、大小及路径集合：

| 核对对象 | 实际结果 |
|---|---|
| 完整设计目录 | 54个物理文件；无缺失、无未登记文件 |
| FILES登记的文件 | 53/53大小与SHA-256一致；FILES自身按既有规则单独核验Git对象 |
| FILES自身Git对象 | `ea8b14b69239641f1637ac78e9697c6e0327f257` |
| 高清PNG | 27张，包含24个命名状态及3个补充尺寸 |
| 交互与编辑源 | Prototype、Gallery、13个源码/字体依赖文件及原许可均已在目录 |
| 字段迁移表 | 实际CSV读取49条字段/聚合记录，不解释为49个独立控件 |
| 原生源码快照 | SOURCE-STATE登记的25个文件全部与本次main大小及SHA-256一致 |

原型SHA-256为 `d87b87dcd419a9295357f37e43024a812bd492c9d203a2fc1e5814e2ffb3f941`。本次不重复上传相同原件、不新造一套设计包、不累计旧V1/V2的图片数量。本工作包的核对只是设计文件与源码身份检查，以及静态代码审查；没有运行新的业务测试、WPF、SDK、安装或现场验收。

资料检查另执行了现有`tools/verify_archive.py --root . --scope public`：本次修改树和独立检出的原始基线均为FAIL，各有完全相同的298项既有问题，新增问题为0，本次变更文件无报错。既有项为原型敏感标记3项、字体扩展6项、历史引用22项、元数据标记2项、历史PPTX媒体类型265项；这些是归档检查分类，不能直接解释为298个运行缺陷或已发生凭据泄露。本次没有修改校验器、登记新豁免或删除历史资产来改成PASS。文档差异格式及本次文件/manifest身份另行核验。

## 工作包

| 包 | 既定内容 | 当前状态与后续 |
|---|---|---|
| PS-00 | 实际截图、最新代码和商业桌面参考核对，撤出旧参数主定义 | 已有设计裁决；本次更新合并事实及当前执行入口，后续只清理新发现的冲突 |
| PS-01 | 同窗专注设置工作区、四分类直接对象目录、搜索与自适应表单 | 主要源码已合并；补齐当前对象运行上下文和既有诊断定位，完成原生保真走查 |
| PS-02 | 自动字段/候选校验，一次保存审阅，真实差异与保存/运行区分 | 主要源码已合并；修复无变更保存、敏感字段编辑，并验证原CAS/Unknown/无效输入保护 |
| PS-03 | 具体命名的模板、备份、加密草稿、安装、旧设置与恢复任务 | 入口已实现；核验适用任务可达及草稿保留，不恢复含混的“维护与迁移”总入口 |
| PS-04 | 可编辑离线原型、逐张高清PNG、字段去向和统一视觉规格 | 已完整进入main，53个登记文件身份全部匹配；保留原件 |
| PS-05 | 受影响构建与必要风险验证、Windows原生收口、提交与证据 | 仍待最终源码对应的Windows原生任务、尺寸/DPI及新版介质交付；原型和历史CI不能替代 |

六项商业质量为完成条件：重点清楚、组织一致、密度合适、细节完整、操作连贯、真实显示可靠。以完整工业任务闭环实现，不选择最小演示实现，不另造通用配置/部署平台。

## 本次源码核查确定的剩余工作

以下为固定基线的静态调用链判断，不冒充Windows复现或已修复结论。执行时先在最新代码确认是否仍存在。

| 项目 | 证据与实际差距 | 完成标准 |
|---|---|---|
| 无变更仍可保存 | [MaintenancePage](../../src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml)保存仅绑定`CanStartOperation`；[审阅模型](../../src/Ipc.Application.ParcelFlow.Desktop/ConfigurationReviewPresentation.cs)的`PrepareConfigurationReviewAsync`计算空差异后仍返回可审阅；`CanSubmitReviewedConfiguration`未要求实际变更。[StageConfigurationAsync](../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)只对同值Pending去重，没有Pending且候选等于Effective时仍可Stage | 正确表达未修改、无效输入、Pending及Unknown；没有实际变更不创建无意义待重启修订；原提交核对入口仍可达，保留CAS及审计 |
| 敏感字段编辑不一致 | [统一字段控件](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Capabilities.cs)的`Parameter`只区分boolean/choice/普通输入；[Input](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.cs)创建普通TextBox。平台[字段契约](../../src/Ipc.Platform/ExtensionContracts.cs)与[真实扩展示例](../../examples/IntegrationExtension/parameters.json)已有`Sensitive=true/secret-reference`，审阅层却已掩码 | 首配与日常共用控件遵守已有敏感声明；显示、审阅及输出一致保护引用，不能为掩码丢失真实值或误清空；不新造凭据系统。现有Sportmaster RCS14项没有该类型，本结论不等于生产密码泄露 |
| 当前对象运行上下文不完整 | V3[相机设计图](../ui/parameter-settings-v3-20261020/screens/01-left-camera.png)及HANDOFF要求运行连接/运行版本和当前对象“连接诊断”。上述原生控件主要构建参数与安装身份，没有按选中实例进入诊断的直接入口或对应运行摘要绑定 | 将当前相机/RCS与真实运行快照、已有诊断路径关联；编辑地址与运行地址明确区分；复用已支持诊断，不引入无合同Probe或业务试单 |
| 原生与发行证据缺口 | [V3 QA](../ui/parameter-settings-v3-20261020/QA.md)明确Windows/DPI/SDK/RCS/现场NOT_RUN；[合并发布记录](../runtime/publication-pr78-pr79-20261021.md)明确PR79旧CI不覆盖加入V3后的组合，1.6.1未重建 | 使用最终冻结源码执行必要原生验证，取得真实WPF截图与键鼠/缩放记录，并生成同源安装介质和启动证据；不能重标旧包 |

## 限定与必要验证

四分类仍编辑一个完整候选；参数所有权沿原映射。真实安装决定字段和组件，Application决定实例与角色。保存运行参数显式审阅，显示偏好沿独立本机偏好。无安全连接测试契约时保留诊断和明确未验证；不能抢占海康设备或发业务POST充当测试。

必要场景：指定相机修改且不串左右；数值Binding失败及隐藏字段错误定位而其他输入保留；跨对象差异审阅；无变更不保存；敏感引用首配/日常共模；保存与运行版本分离、Unknown原次核对；RCS共享异常和按对象进入诊断；返回作业留稿与实际关闭保留/放弃。涉及当前任务投影时才补对应左右连续处理回归，不为改表单重跑所有业务套件。

沿现有原生验证入口覆盖受影响构建、保存/恢复与输入风险；目标尺寸优先1366×768和现场工控机实际分辨率，核对Windows100%/125%及现场实际缩放，800×420仅作既有最小兼容范围。验证焦点、选区、中文输入、滚动、错误和底部动作可达；不把浏览器逻辑视口当Windows实测。必要发行检查覆盖最终介质安装、首启/已有配置入口及启动冒烟。只执行实际改动所需测试及仓库必需门禁，不堆测试数量。

## Codex执行提示

```text
任务：完成 yuan2go/importer-pc 参数设置 V3 的原生交付收口，沿用 WP-IPC-PARAMETER-SETTINGS-02（PS-05及实际缺口），直接完成实现与交付。

1. 从执行时最新 main 固定SHA并检查并行变更。先详读AGENTS、docs/INDEX、框架及决策、PARAM-UX-02、V3的README/HANDOFF/QA/字段映射、本工作包与实际源码；编码前清理过期、冲突的当前设计和指引，修正索引及替代关系，保留原资料、编号、哈希和历史证据。PR78/79已合并，复用现有实现。

2. 以 docs/ui/parameter-settings-v3-20261020 的高清图和可交互原型为参数体验基线，在现有C#/WPF内采用工业级最佳实践完成完整可靠的实现，不选择最小实现。使用项目已有UI/UX Pro Max（--stack wpf）及Impeccable Operate复核信息层级、布局和交互；保留单目录、集中表单、统一保存审阅和具体任务工具，避免重复框架与无依据抽象。

3. 优先复核并修复：无变更仍能保存并生成待重启修订；Sensitive/secret-reference被普通文本控件显示；当前相机/RCS缺少真实运行摘要和按对象进入已有诊断的直接入口。继续按设计与实际原生走查补齐差距。状态、禁用原因、错误定位、搜索、无效原始输入、焦点/选区/草稿必须真实且连贯；刷新及耗时操作不干扰填写。

4. 保持系统/业务模块/驱动/组件归属、业务决定的固定左右实例、独立相机参数、单共享RCS和业务条码正则；首配只在首次启动，复用同一模型。继承会话字段与启动回退修复。复用唯一配置账、Revision/RuntimeId/CAS、冻结候选和Stage/Inspect；Unknown只核对原提交，保存与运行分开，重连不应用Pending。UI不得绕过暂停、维护授权和Application守卫；无安全Probe合同不发业务试单、不伪造成功。

5. 只做改动风险所需测试与仓库必需门禁：受影响构建、关键保存/恢复与敏感字段回归、原生输入/草稿/错误/诊断任务、目标Windows尺寸/DPI/键鼠和必要安装启动冒烟。实际检查文字层级、密度、统一操作与状态、焦点和滚动；修复真实失败，不通过删断言、吞异常换PASS，不重跑无关套件。原型、交叉构建、原生、真实SDK/RCS及现场结果分别记录；环境不足时完成不受影响工作并写明具体待验项。

6. 交付最终同源的WPF截图、必要证据、可安装介质、运行说明和准确文档/manifest；旧1.6.1不包含V3，不能换标签冒充新版。业务代码在开发分支提交、推送并创建main PR，遵守保护规则、不强推；报告实际commit/PR、完成项及验证边界。具体类划分和实现由你判断，保持现有业务合同和连续作业稳定。
```
