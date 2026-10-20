# Sportmaster V2：实现与证据入口

当前实现见 [UIR-00～06 实施记录](../runtime/ui-task-redesign-01.md)，目标仍为 [V2 设计集](sportmaster-task-redesign-v2-20261020/README.md)、[设计裁决](../design/sportmaster-task-redesign-01.md)及 [工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)。基准 main `de2b045`；实现分支 `codex/sportmaster-v2-ui`。

WPF 已按系统、业务模块、驱动、组件组织统一参数；五步首配独立；作业页固定相机、扫描、RCS任务三组；新增 waveNo、响应字符串 quantity 的兼容传递与关联显示。保存、待重启、原请求、共享 Application、受控生命周期及唯一心跳机制继续由原 Application 管理。实现、验证范围、运行方式与高清图由实施记录统一登记，不从原型推导生产能力。

原 [V1设计](sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)的48张PNG、58项字段及39项操作保留；V2的67张参考PNG及离线原型也保持原字节。旧 [v1.1](ss-ui-vis-001-v1.1/README.md)不再是现行页面主定义。[实施前状态全文](../archive/pre-ui-task-redesign-implementation-20261020/docs__ui__STATUS.md.txt)保留为历史证据。

历史 [PARAM-01](../runtime/parameter-refactor-01.md)、[单窗口安装](../runtime/installer-single-window-01.md)、[心跳](../runtime/rcs-heartbeat-01.md)和各发行介质保留原 SourceCommit 与结果。新的 WPF合成、Windows本机键鼠/125%缩放分别记录；目标工控机、Win10 22H2、其他DPI、真实SDK/设备/RCS及现场仍需各自验收。新局部验证不代表旧HTML或全部历史RV已关闭。
