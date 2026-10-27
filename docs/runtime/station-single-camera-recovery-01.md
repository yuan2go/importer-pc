# 单相机、窗口显示与管理密码恢复

工作包：[STATION-SINGLE-CAMERA-RECOVERY-01](../work-packages/WP-IPC-STATION-SINGLE-CAMERA-RECOVERY-01.md)。开发阶段自有产品版本保持 1.0.0。

SM 的显示偏好现在仅保留窗口打开方式及标准/紧凑布局。站点显示名称与目标效率原来主要用于本机摘要和悬停提示，不参与取格口或效率控制，已从 SM 设置页移除。共享偏好机制及历史本机文件保留，其他业务不受影响；SM 不再因这两项隐藏显示值触发未保存提示。

左右相机可仅配置其中一台，另一台保持稍后配置。只要已启用相机的实际读码能力、RCS 及原业务条件就绪，就可使用对应投递位；未配置的位置不启动、不接包。两台相机均未配置或 RCS 未配置仍阻断作业，健康确认、权限、容量、实体边界和 Unknown 原请求守卫继续有效。单相机不会补造另一侧的健康状态或条码。

管理模式菜单只保留实际动作及到期提示。“重新验证”改为“进入管理模式”，删除“管理权限未开启/已开启”重复行。登录窗口新增“忘记管理密码？”，输入用户指定的固定根密码和两次新密码后重置。根凭据在程序中以固定校验哈希实现，密码输入不写入日志或凭据账；新的管理密码继续用随机盐和强哈希保存。

恢复密码连续 5 次错误后锁定 5 分钟，锁定状态随受保护存储持久化，正常管理密码仍可使用。成功恢复撤销全部旧管理会话和维护委托，并要求使用新密码再次进入；根密码不能直接作为管理登录。遗失或损坏凭据账仍保留原件并拒绝覆盖，恢复仅处理忘记当前有效密码。受保护安装继续通过原 System 管理服务，新增协议请求、UI、辅助进程及宿主须同源更新。

固定根密码由用户明确要求；知道该固定值的人可以重置本机软件管理密码。此能力不提供 Windows 管理员权限。

## 相机能力

MV-PD010003-06M-12C 的当前采集、解码状态、参数及依赖统一见[海康运行说明](hikrobot-reader-01.md)。单相机准入要求本位实际业务就绪；连接、取帧、自检均不能代替该确认。

## 验证与运行

限定结果见[验证记录](evidence/station-single-camera-recovery-01/README.md)。常用复测入口：

```powershell
. ./scripts/project-environment.ps1
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll $PWD --station-single-camera
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD --station-settings
```

原生窗口、环回 RCS、密码恢复和合成输入分别验证。真实相机触发/取帧/算法条码、真实 RCS、安装替换及现场人员可用性按实际条件记录，不继承先前 SDK 初始化或模拟测试的 PASS。
