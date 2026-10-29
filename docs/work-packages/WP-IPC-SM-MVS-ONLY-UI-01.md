# SM-MVS-ONLY-UI-01

2026-10-29，用户再次指出相机驱动中不应有 TCP 连接内容，当前走 MVS SDK。基准 main `924a77ca`；原工作区 `codex/current-contract-cleanup-01` 及用户未提交文件保留，实施位于 D 盘独立工作树。

D-SM-MVS-UI-01：继承 [MVS-SOFTWARE-READER-01](../runtime/mvs-software-reader-01.md) 和 [CAMERA-CONFIG-STARTUP-02](WP-IPC-CAMERA-CONFIG-STARTUP-02.md)。首配、日常相机驱动固定显示 MVS SDK 采图＋软件识别，删除正常页及不适用原设置提示中的 TCP 文案，不提供相机端口或连接方式选择。原设置只在用户明确重新配置时重置本相机参数，另一相机、共享编码、保存核对及受控重启保持。

本包仅纠正当前相机页和框架顶部的过期当前说明。共享驱动、其他业务、历史配置原件、历史 D/RV、冻结请求及旧验收不改写，不将通用 TCP 代码视为当前 SM 页面支持方式。

RV-SM-MVS-UI-01：PASS_NATIVE_WPF，沿现有 `--hikrobot-sdk-editing` 原生夹具核对首配、日常左右相机、紧凑窗口和原设置重新配置；增加所有文本块及文本框不出现 TCP、固定 MVS SDK 身份的断言。构建 0 警告、0 错误，原生退出码 0。实际结果及边界见[实施记录](../runtime/sm-mvs-only-ui-01.md)；不代表安装、真实设备或现场验收。
