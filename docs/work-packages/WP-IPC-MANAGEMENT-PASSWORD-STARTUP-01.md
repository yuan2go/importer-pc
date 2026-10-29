# MANAGEMENT-PASSWORD-STARTUP-01

2026-10-29 用户报告已装程序无法设置管理密码，维护进程退出码 -532462766，同时要求停用 GitHub CI。基线 main `7af0be61`，隔离 D 盘分支 `codex/management-password-crash-01`；并行 PR148 的相机/Python 验证不纳入本包，原工作区未提交材料不动。

本机 Application 事件确认 `ParcelStartupServices.Handle` 在回执通道前被 `RequireExtensionInstallerAuthority` 拒绝。机器 `EnableLUA=0`，日常 NORMALUSER 进程直接 runas 仍得到受限令牌。MODE-02 安装维护批准、管理密码验证、不可覆盖和生产准入继续有效，不修改全机 UAC/账户/ACL 政策。

实施范围：复用当前 Windows 桌面授权启动同源受保护 helper，核验同用户、会话、启用的管理员组、实际映像、启动时间和管道 PID；helper 等待客户端身份确认后才能执行。权限不足沿可信终态反馈，避免未处理异常。窗口首设完整显示，长错误局部滚动、动作始终可达。共享维护入口继承本修复；不重建权限账，不设置当前站点的测试密码。

停用全部远端 Actions 工作流并取消运行中的 CI；当前两个 push/PR 工作流改为仅手动声明，维持远端停用状态。不自动重新启用或调度。验证在本机执行，失败原样保留；分支经 PR 提交，不绕过 main 保护。

必要验证：真实受限进程→管理员 helper→身份确认/退出回执；管理服务首次设置/不可覆盖/锁定/撤权；原生 WPF 常规/小窗口/长失败/Tab/密码缓冲区；Sportmaster 桌面与 helper 构建、同源离线包及启动冒烟。真实 UAC 开启的批准/取消、其他用户桌面、目标 IPC/设备/RCS/现场分项保留未验。当前结果和运行方式见[实施记录](../runtime/management-password-startup-01.md)。
