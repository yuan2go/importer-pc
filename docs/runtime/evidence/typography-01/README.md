> 追加原生对照及真实字体入口续修见[controlled-review](controlled-review/README.md)；本页及下列原图/日志保持首轮身份。

# TYPOGRAPHY-01 · 本机原生证据

基线为 PR97 `37b27a9` + main `c5ac51d` 的隔离整合 `e5ed46d`。生产代码 `9e6c628` / `a6065f0`，紧凑异常区续修 `f4f6045`；`169fcca`仅增加保存核对原生截图。每次运行源码与原日志见 [RESULTS.json](RESULTS.json)，不将中间PASS扩张为后续整树验收。

Windows 11 23H2 x64（22631），.NET 10.0.13，实际125%系统缩放。真实 HWND/Dispatcher，合成端口与数据；图片像素尺寸由实际DPI决定，客户区DIP见每条CAPTURE。前后使用同一夹具场景，时间/合成实体ID随运行生成。无客户截图、生产数据或凭据。

## 必要检查

| 检查 | PASS项 | 源码 | 原始日志 |
|---|---:|---|---|
| task-hierarchy | 165 | `f4f6045` | [log](ci-fix/local/task-hierarchy/WINDOWS-WPF.log) |
| task-hierarchy-parameters | 9 | `9e6c628` | [log](checks/task-hierarchy-parameters/WINDOWS-WPF.log) |
| internal-pages | 25 | `a6065f0` | [log](checks/internal-pages/WINDOWS-WPF.log) |
| visual-productization | 115 | `f4f6045` | [log](ci-fix/local/visual-productization/WINDOWS-WPF.log) |
| parameter-v3-only | 23 | `169fcca` | [log](checks/parameter-v3-only/WINDOWS-WPF.log) |
| parameter-shell-polish | 30 | `9e6c628` | [log](checks/parameter-shell-polish/WINDOWS-WPF.log) |
| management-mode | 28 | `a6065f0` | [log](checks/management-mode/WINDOWS-WPF.log) |
| installer-first-run-ui | 88 | `a6065f0` | [log](checks/installer-first-run-ui/WINDOWS-WPF.log) |
| startup-experience | 172 | `a6065f0` | [log](checks/startup-experience/WINDOWS-WPF.log) |

**首轮9组666项PASS；当前各组最新选定记录合计655项PASS**；另[发布目录字体检查](published-font/WINDOWS-WPF.log)11项PASS。实际GlyphRun证明Regular/Medium分别加载内嵌OTF 400/500，代码字符用Consolas，显式缺失家族用Microsoft YaHei UI；未发现样本文字缺字。见[实际字体URI](published-font/FONT-RESOLUTION.json)和[字体截图](published-font/typography-font-resolution.png)。没有禁用全机网络；程序集资源读取和本地核验不需联网。

[UI构建](build-final-02.log)、[跨平台验证工程构建](portable-build-final.log)、[安装器构建](installer-final-02.log)均0警告/0错误；[win-x64本地发布](publish-final.log)、[发布资源/许可核验](PUBLISH-RESOURCES.json)、[环境报告](publish-startup.stdout.txt)和[退出码](publish-startup.exit.txt)通过。[交付文档测试](release-documents-test.log)2项通过；保留[资源核验原脚本](publish-resource-check.py.txt)。字体探针在独立目录中以发布DLL替换所有Ipc程序集，其字体程序集哈希与发布清单一致。没有制作正式介质或执行安装。

中间发现并修复了经典800×420内容区、日志展开上下文不足完整一行、首次密码窗口空反馈占位三个问题；[失败原件](iterations/README.md)保留，原断言没有删除、跳过或降低。

## 同场景前后对照

以下18组均为本次实际运行的未加工原生截图；完整配对和来源见[SCREENSHOTS.json](SCREENSHOTS.json)。

| 场景 | 前 | 后 |
|---|---|---|
| supply-station-工作台-1366 | [前](before/task-hierarchy/supply-station-工作台-1366.png) | [后](ci-fix/local/task-hierarchy/supply-station-工作台-1366.png) |
| supply-station-工作台-800 | [前](before/task-hierarchy/supply-station-工作台-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-工作台-800.png) |
| classic-工作台-800 | [前](before/task-hierarchy/classic-工作台-800.png) | [后](ci-fix/local/task-hierarchy/classic-工作台-800.png) |
| supply-station-long-800 | [前](before/task-hierarchy/supply-station-long-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-long-800.png) |
| supply-station-Unknown-800 | [前](before/task-hierarchy/supply-station-Unknown-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-Unknown-800.png) |
| supply-station-Rejected-800 | [前](before/task-hierarchy/supply-station-Rejected-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-Rejected-800.png) |
| supply-station-Conflict-800 | [前](before/task-hierarchy/supply-station-Conflict-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-Conflict-800.png) |
| supply-station-stale-1366 | [前](before/task-hierarchy/supply-station-stale-1366.png) | [后](ci-fix/local/task-hierarchy/supply-station-stale-1366.png) |
| supply-station-unadmitted-1366 | [前](before/task-hierarchy/supply-station-unadmitted-1366.png) | [后](ci-fix/local/task-hierarchy/supply-station-unadmitted-1366.png) |
| supply-station-作业记录-800 | [前](before/task-hierarchy/supply-station-作业记录-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-作业记录-800.png) |
| supply-station-日志查询-800 | [前](before/task-hierarchy/supply-station-日志查询-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-日志查询-800.png) |
| supply-station-log-range-800 | [前](before/task-hierarchy/supply-station-log-range-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-log-range-800.png) |
| supply-station-logs-command-Unknown-800 | [前](before/task-hierarchy/supply-station-logs-command-Unknown-800.png) | [后](ci-fix/local/task-hierarchy/supply-station-logs-command-Unknown-800.png) |
| parameters-供包台标识-1366 | [前](before/task-hierarchy-parameters/parameters-供包台标识-1366.png) | [后](after/task-hierarchy-parameters/parameters-供包台标识-1366.png) |
| parameters-右相机-800 | [前](before/task-hierarchy-parameters/parameters-右相机-800.png) | [后](after/task-hierarchy-parameters/parameters-右相机-800.png) |
| parameters-right-error-800 | [前](before/task-hierarchy-parameters/parameters-right-error-800.png) | [后](after/task-hierarchy-parameters/parameters-right-error-800.png) |
| history-selected-1366 | [前](before/internal-pages/history-selected-1366.png) | [后](after/internal-pages/history-selected-1366.png) |
| history-filters-800 | [前](before/internal-pages/history-filters-800.png) | [后](after/internal-pages/history-filters-800.png) |

## 其他代表页面

- [最小窗口日志上下文与完整结果行](after/management-mode/management-log-context-800.png)
- [保存核对与失效状态](after/parameter-v3-only/v3-save-review.png)、[已保存待重启](after/parameter-v3-only/v3-saved-pending.png)
- [首次密码设置](after/installer-first-run-ui/password-first-setup.png)、[安装选项](after/installer-first-run-ui/installer-options-default.png)、[长路径核对](after/installer-first-run-ui/installer-confirm-long.png)
- [首次配置授权入口](after/installer-first-run-ui/first-run-unconfigured.png)、[第一步](after/startup-experience/startup-step-1.png)、[保存前检查](after/startup-experience/startup-step-5.png)、[小窗口](after/startup-experience/startup-small.png)

原生审看确认：标题克制，普通界面常规字重；有效格口/异常优先；长值可滚动选择；参数错误仍定位原字段，保存仍区分Pending/Effective；日志保留原文、范围和结果行。最低边界下长值/上下文需要局部滚动，没有承诺任意长度同时完整铺开。

## NOT_RUN

实际100%与150%系统DPI、目标IPC、物理键鼠硬件、实际安装/UAC、SDK/相机/RCS和生产现场均未执行。既有安装夹具中的等效窗口尺寸名称不等于系统DPI已改变。HTML、原型示意及历史PASS不作为本次原生证据。复现方式见[运行记录](../../typography-01.md)。

## 首次CI与续修

[CI续修记录](ci-fix/RESULT.json)保留首次托管失败：Windows Server 2025实际100%缩放下，800×420的未准入输入加原Unknown责任挤压条码。原门禁未变；紧凑位标题/结果框/条码标签仅减少空白，本机该场景条码底184、正文视口211.2 DIP，原先仅余2.4 DIP。两组受影响本机原生检查280项重新通过（此次115项视觉检查未重复其可选首配11项；首配172项独立结果保留），当前表格/配对链接指向续修截图，原after图片及原日志保留。托管复跑状态在PR检查中更新；这不替代本机Windows 11实际100%/150%验证。
