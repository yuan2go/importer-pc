# 参数设置 V3：当前设计与实现入口

当前参数页面采用 [V3设计包](parameter-settings-v3-20261020/README.md)、[PARAM-UX-02](../design/parameter-settings-experience-02.md) 和 [PS-00～05](../work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)。一个四类对象目录、专注设置工作区与统一保存审阅，替代V2参数叠层导航和“维护与迁移”总入口。实际状态见 [V3实施记录](../runtime/parameter-settings-experience-02.md) 与 [QA](parameter-settings-v3-20261020/QA.md)。

本轮交付27张高清PNG、可编辑离线原型及字段/状态/任务交接；它们为合成演示，不是WPF实机截图。源码读取main `837370e`，已整合最新main `bf61337` 的PR76/77。原生修改由开发分支进入main PR，Windows最终源验证另记，当前1.6.1安装包没有因此自动更新。

[V2作业/首配目标](sportmaster-task-redesign-v2-20261020/README.md)、[原实施](../runtime/ui-task-redesign-01.md)及其WPF、键鼠和发行证据保留各自SourceCommit。相机/扫描/RCS任务三组、waveNo/taskId/skuId/响应quantity、独立首配、原请求与唯一配置账不因参数美学调整而重写。

V1的48张PNG和V2的67张参考PNG保持原字节；[v1.1](ss-ui-vis-001-v1.1/README.md)为历史方案。它们不叠加为本次页面或文件数量。原型、交叉编译、Windows、精确SDK/RCS和现场分别计证，未执行部分不得扩张为PASS。
