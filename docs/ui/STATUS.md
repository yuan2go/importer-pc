# UI状态：目标设计、现有实现与证据

## 当前目标

[20261020完整设计入口](sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)是本次Sportmaster任务与参数配置重设计目标。依次阅读 [实施裁决](../design/sportmaster-task-redesign-01.md)、[UIR工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)和 [Codex实施提示](../work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)；提交状态见 [入库记录](../runtime/ui-task-redesign-01-publication.md)。

**本次只交付设计、分析和计划。UIR-01～06未移植WPF，不能据原型或PNG声称新软件已实现。** 设计为45个命名界面状态、48张PNG；95是含重复格式、源码、QA及ZIP的原文件总数。ZIP因接口限制以11分片及校验恢复保存，不构成新增设计。

## 现有WPF必须继承

复核基准 `e2e69292899c709056ce9ab6159bf01f6f18de6c` 已含PR62/63/64/66/67/68。默认supply-station与辅助窗口共享Application；字号/状态层级、OneWay诊断、单码拒绝分类、旧格口失效、原生日历及classic最小区修复均已存在。正常作业最低有效客户区800×420 DIP，缩小保护和恢复后人工确认不能取消。

左右读码Reader与可选采图Camera不同；海康按真实五字段配置，无演示9000端口。对象页编辑同一完整候选，沿唯一CAS/Pending/Effective账与受控重启；Reconnect不应用Pending。当前RCS无权威Query、仅本地Health，不伪造测试或查询成功。菜鸟品牌、单EXE、英文SM命名、快捷方式和BUILD-INFO已实现。

新的对象导航、字段错误定位、差异与生效解释、任务诊断、跨页左右摘要仍按UIR-01～06实施。无需重做上述机制，也不能只交一张首页代替完整任务闭环。

## 验收与历史

[组合原生证据](../runtime/parallel-task-reconciliation-20261020-02.md)保留233项组合和9项完成页检查的原来源；[SM安装发行](../runtime/installer-release-info-01.md)的41项实际升级/启动属于原7c源码和原主机Simulation。它们不是本次新UI的PASS。原生HWND backing surface与实际物理屏幕、输入、多DPI分别记录；真实相机/SDK、RCS和现场仍按实际未验条件处理。

[v1.1及34张同源PNG](ss-ui-vis-001-v1.1/README.md)是历史模拟设计，不是本次强制页面或布局。原 [0e79e73截图集](../archive/sportmaster-native-gallery-20261020-0e79e73/README.md)、[PR58视觉结果](../runtime/sportmaster-visual-productization-01.md)和 [日期修复](../runtime/sportmaster-visual-main-closure-01.md)保持各自字节、源码及原结论；历史D/RV与失败不倒写。清理前状态页见 [归档](../archive/pre-ui-task-redesign-20261020/README.md)，公开材料遵守 [PUBLICATION](../baseline/PUBLICATION.md)。
