# 窗口比例与首次设密批准 · 本轮证据

源码 `c6c5cd5285631e1e4ba5ac3990cfff641ff75d49`，main `0bf86a0a588b75b95aa1963e8fe88dad976ca803`。映射、根因、范围及未验事项见[实施记录](../../../installer-first-run-ui-02-bootstrap-layout.md)。原证据各守原SourceCommit；未使用用户实机原图冒充本轮输出。

| 检查 | 结果 |
|---|---|
| [原生旅程](NATIVE.log) | Windows125%下真实WPF HWND/Dispatcher：默认尺寸、设密表单无初始滚动、授权取消后的输入清空/可重试、原Tab/焦点、长路径/页面往返、安装失败和未知、失权提交拒绝及同一进程输入恢复通过。安装和UAC取消边界显式注入，不是实际UAC批准/拒绝。 |
| [管理服务回归](MANAGEMENT-CORE.log) | 实际本地强哈希、五位纯数字、边界策略、不可覆盖、错误锁定、到期/退出/修改密码撤权、草稿/完整候选和原请求责任通过。 |
| [真实System服务只读路由](BOOTSTRAP-ROUTE.log) | 新工厂连接当前已装280e4b74的未设密System服务，收到批准要求后到达提权前匹配检查；旧helper和新开发DLL不匹配被拒绝，没有发起UAC/写入密码，服务状态及安装登记不变。不能据此宣称完整批准成功。 |
| 构建 | [Desktop](DESKTOP-BUILD.log)、[Native](NATIVE-BUILD.log)、[Core](CORE-BUILD.log)，均无警告/错误。 |

截图来自实际显示的原生控件内容，不含系统标题栏：

- [安装默认880×560](installer-options-default.png)、[665×460逻辑工作区](installer-options-1024x768-150-equivalent.png)、[长路径确认](installer-confirm-long.png)
- [首配待授权720×440](first-run-unconfigured.png)、[授权到期](first-run-expired.png)
- [首次设密520×480](password-first-setup.png)、[合成UAC取消反馈](password-bootstrap-cancelled.png)

全部系统实际缩放为125%；小窗口验证不等于真实100%/150%DPI或显示模式切换。当前机器的安装、密码、配置未修改，未自动安装新包或连接设备。完整UAC/新包System设密到首配、目标IPC/物理输入和设备验收仍待用户实测。原字节及结果见[RESULT.json](RESULT.json)。
