# UI状态：目标设计、现有实现与证据

## 当前目标

[V2 设计入口](sportmaster-task-redesign-v2-20261020/README.md)是本次Sportmaster任务与参数配置重设计目标。依次阅读 [实施裁决](../design/sportmaster-task-redesign-01.md)、[UIR工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)和 [Codex实施提示](../work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)；本轮材料见 [V2 发布记录](../runtime/ui-task-redesign-v2-publication-20261020.md)，[V1 入库记录](../runtime/ui-task-redesign-01-publication.md)只保留原发布事实。

**本次交付V2设计、离线模拟和指引，不修改生产代码或执行构建/测试。PARAM-01已有部分WPF/参数实现，仍是未完成快照；不声称全部UIR未移植或完整V2已验收。** 当前参数为系统/业务模块/驱动/组件四类；首次配置仅为首次启动的五步独立流程；左右相机各有相机/扫描/任务三组固定信息，任务含格口、waveNo、taskId、skuId 与返回数量。界面/PNG 计数以 V2 索引为准，本轮不新增Windows或真实设备验收，历史记录各守原来源。

## 现有代码必须继承，快照继续完成

最新复核 `ba80d0ea90d86b24f9d9eebd20d075da34320972` 已合入PR69/70/71/72，当前参数事实见[PARAM-01实施中记录](../runtime/parameter-refactor-01.md)。默认工作台与辅助窗口共享Application；字号/状态层级、OneWay诊断、旧格口失效、原生日历、classic最小区及800×420 DIP保护继续继承。

| 已有增量 | V2继续完成 |
|---|---|
| 四分类 `ParameterEditorModel`/`ParameterSettingsControl`、真实描述字段与错误定位。 | 系统/业务模块/驱动/组件按用户目标重排；预算与请求规则归业务模块，取格口zone/project/station归系统，实例编码用途明确。一个用途不重复可写。 |
| 首配/日常共模、缺配离线编辑路径、唯一CAS/加密草稿及保存核对。 | 五步独立首配、对象草稿/差异、NotSaved/Conflict/Unknown/Pending及应用/运行确认的完整呈现。已有源码不等于已验证闭环。 |
| 完整相机候选→Application有界筛选→SelectedCode、原候选与规则证据。 | 扫描区显示来源与筛选状态，保留本地样例、零匹配/歧义/错误/预算与旧null兼容；不猜生产正则。 |
| taskId/skuId已有解析和应用传递。 | 稳定三组显示；补waveNo/响应quantity兼容传递与历史/当前投影，不串包或用上一包回填。 |
| 海康当前1.0.3真实六项，含心跳bcr-code；RCS已有周期心跳。 | 实例页区分取格口映射与心跳编码，组件保留心跳间隔/协议/编号；Health分清HTTP交付与应用确认，Query=false，重连不应用Pending。 |
| 单窗口安装、一次授权、固定桌面入口、原helper/责任守卫与BUILD-INFO。 | 复用必要视觉衔接，不能重建安装器或借原介质的PASS证明参数/V2完成。 |

左右Reader与可选采图Camera不同；真实驱动无演示9000端口，Sportmaster不新增包裹预览、PLC或逐包人工提交。四类与首配只是同一配置候选的编辑范围。目标三组布局、完整对象维护、六任务和六项商业质感仍按UIR-01～06逐项完成，不能把缺真实设备当作放弃其余界面的理由。

## 验收与历史

[组合原生证据](../runtime/parallel-task-reconciliation-20261020-02.md)保留233项组合/9项完成页来源；[单窗口安装](../runtime/installer-single-window-01.md)的27b0d8f/58项、[心跳](../runtime/rcs-heartbeat-01.md)的3f8a58ba原验证、[旧安装发行](../runtime/installer-release-info-01.md)的7c/41项均保留各自源码和环境。[PR69～72发布](../runtime/publication-pr69-pr71-20261020.md)未测试最终合并树；这些记录不是PARAM快照或V2的PASS。原生HWND backing surface与实际物理屏幕、输入、多DPI分别记录；真实相机/SDK、RCS和现场仍按实际未验条件处理。

[V1 任务重设计](sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)的45状态/48张PNG、95个原文件及原哈希保留为历史；95包含源码/格式/QA/ZIP，不是页面数，分片存储也不增加设计数。其侧栏首配和旧参数分类被V2替代。

[v1.1及34张同源PNG](ss-ui-vis-001-v1.1/README.md)是历史模拟设计，不是本次强制页面或布局。原 [0e79e73截图集](../archive/sportmaster-native-gallery-20261020-0e79e73/README.md)、[PR58视觉结果](../runtime/sportmaster-visual-productization-01.md)和 [日期修复](../runtime/sportmaster-visual-main-closure-01.md)保持各自字节、源码及原结论；历史D/RV与失败不倒写。清理前状态页见 [归档](../archive/pre-ui-task-redesign-20261020/README.md)，公开材料遵守 [PUBLICATION](../baseline/PUBLICATION.md)。
