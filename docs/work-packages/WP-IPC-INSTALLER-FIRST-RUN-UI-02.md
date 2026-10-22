# WP-IPC-INSTALLER-FIRST-RUN-UI-02

执行日期：2026-10-22。执行时 fetch 的最新 main：`0bf86a0a588b75b95aa1963e8fe88dad976ca803`。本地实现继承其后尚未合并 PR88 的 `793d092851052c63663f19b5de8d2b7703392d8d`（管理模式及清理后安装修复）；main 是该提交祖先。唯一开放 PR88，未改动其远端。新本地分支 `codex/installer-first-run-ui-02`；原两份未跟踪设计文件保留。不推送、不发布、不连接生产设备。

当前 UI 主定义：[UI-SPEC](../ui/installer-first-run-ui-02/UI-SPEC.md)、[预览](../ui/installer-first-run-ui-02/Preview.html)。本包只基于截图设计，不把预览演示当现有能力。安装位置连续显示两个目录，安装设置包含范围和两个独立快捷方式；确认使用同一已审阅请求。安装阶段来自实际 helper，成功依完整安装回执；安装不启动业务。首配入口分未设密、待验证、失权和服务不可用，复用真实管理模式及维护服务。失权禁止保存/应用，原未保存输入仅在进程中保留。

旧布局与模糊授权提示由本包替代；首次五步和参数内页不扩写。密码策略、加密存储、服务层再校验、Windows UAC、操作员身份与管理模式保持原边界。D/RV及历史验收结论不改。旧索引见[原字节归档](../archive/pre-installer-first-run-ui-02/README.md)。

交付与必要验证结果见 [实施记录](../runtime/installer-first-run-ui-02.md)。实际 Windows 原生、合成服务、UAC/安装事务及真实设备分别记录，未执行项不记 PASS。
