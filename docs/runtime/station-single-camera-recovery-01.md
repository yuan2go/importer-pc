# 单相机、窗口显示与管理密码恢复

工作包：[STATION-SINGLE-CAMERA-RECOVERY-01](../work-packages/WP-IPC-STATION-SINGLE-CAMERA-RECOVERY-01.md)。开发阶段自有产品版本保持 1.0.0。

SM 的显示偏好现在仅保留窗口打开方式及标准/紧凑布局。站点显示名称与目标效率原来主要用于本机摘要和悬停提示，不参与取格口或效率控制，已从 SM 设置页移除。共享偏好机制及历史本机文件保留，其他业务不受影响；SM 不再因这两项隐藏显示值触发未保存提示。

左右相机可仅配置其中一台，另一台保持稍后配置。只要已启用相机的实际读码能力、RCS 及原业务条件就绪，就可使用对应投递位；未配置的位置不启动、不接包。两台相机均未配置或 RCS 未配置仍阻断作业，健康确认、权限、容量、实体边界和 Unknown 原请求守卫继续有效。单相机不会补造另一侧的健康状态或条码。

管理模式菜单只保留实际动作及到期提示。“重新验证”改为“进入管理模式”，删除“管理权限未开启/已开启”重复行。登录窗口新增“忘记管理密码？”，输入用户指定的固定根密码和两次新密码后重置。根凭据在程序中以固定校验哈希实现，密码输入不写入日志或凭据账；新的管理密码继续用随机盐和强哈希保存。

恢复密码连续 5 次错误后锁定 5 分钟，锁定状态随受保护存储持久化，正常管理密码仍可使用。成功恢复撤销全部旧管理会话和维护委托，并要求使用新密码再次进入；根密码不能直接作为管理登录。遗失或损坏凭据账仍保留原件并拒绝覆盖，恢复仅处理忘记当前有效密码。受保护安装继续通过原 System 管理服务，新增协议请求、UI、辅助进程及宿主须同源更新。

固定根密码由用户明确要求；知道该固定值的人可以重置本机软件管理密码。此能力不提供 Windows 管理员权限。

## MV-PD010003-06M-12C 的触发与条码链路

用户确认该型号，尚未在客户端验证触发结果。[海康产品页](https://www.hikrobotics.com/en/machinevision/productdetail/?id=7184)列出该型号；[官方 IDS 产品手册](https://www.hikrobotics.com/cn2/source/vision/video/2026/3/26/机器视觉智能产品手册-V.111.CN.26Q1.1（阅读版）.pdf)将其列为标准型，并说明标准型配合工控机完成复杂多包裹读码。以下结论来自当前实现及本机依赖核对，不代表实际相机已经触发或识码。

当前 `MvsAcquisition.StartControlledAsync` 启动原生采图，`MvsNative.cs` 中的 `Acquire` 调用 `MV_CC_GetImageBuffer`；帧会校验并成对归还给 `MV_CC_FreeImageBuffer`。采图循环只计数图像，没有调用条码识别算法，也不会调用条码观察接收函数。因此即使已经收到有效图像，当前这条实现仍不会产生条码或申请格口，不能据此声称供包成功。

诊断现在明确区分“尚未收到图像”和“已经收到 N 帧但缺少条码识别”。触发设置不符合所选要求、物理身份变化、丢包、帧号间断和不完整图像都会报告采集故障；没有数据会继续等待，均不发布业务 Ready。当前约束为 Mono8 单图像、默认 8 MiB 缓冲上限；该型号 3072×2048 的 Mono8 图像约 6 MiB，尺寸本身没有超过默认上限。

当前仍缺该标准型套件适用的识码算法开发组件及实际输出验证。相机灯亮、SDK 已连接、读到图像、算法得到条码、供包台接到观察和 RCS 接受是不同事实。后续联调须依次核对这些事实，并确认触发源、客户端独占、网络丢包、算法输出和应用接收；不能通过填入模拟码或发布虚假健康状态弥补。

SM 相机入口和默认装配继承当前 main 的内置 MVS：不显示 TCP 切换、端口或独立驱动安装。SDK 的 14 个实际 x64 运行库及告知文件随离线安装包提供，不依赖用户另装 MVS 客户端。新构建重新验证它们的字节和哈希；厂商文件仅在本地交付，不加入 Git。

## 验证与运行

限定结果见[验证记录](evidence/station-single-camera-recovery-01/README.md)。常用复测入口：

```powershell
. ./scripts/project-environment.ps1
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll $PWD --station-single-camera
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD --station-settings
```

原生窗口、环回 RCS、密码恢复和合成输入分别验证。真实相机触发/取帧/算法条码、真实 RCS、安装替换及现场人员可用性按实际条件记录，不继承先前 SDK 初始化或模拟测试的 PASS。
