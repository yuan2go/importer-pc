# 安装、更新、卸载与普通启动 · INSTALL-UPDATE-UNINSTALL-IDENTITY-01

2026-10-28，承接 PR138 卸载结果修复和并行 PR137 的普通令牌启动实现。用户本次异常发生在运行实例创建前；不是数据库、相机或 RCS 已启动后失败。

## 当前纠偏

“从开始菜单正常启动”不足以保证普通权限：安装器、文件管理器或桌面本身可能带提升令牌。日常桌面入口需要主动核对并在必要时创建同一用户的 Windows NORMALUSER 子进程，随后仍执行既有指定账户、完整性、配置、租约和业务守卫。安装、更新、卸载和管理服务的授权边界继续有效；不改全机 UAC、用户账号或现有 ACL，不通过模拟工厂绕过 Production。

四种安装目的由受保护原登记和 SourceCommit 判定。再次运行同源包是修复安装；不同源码是更新；原安装已退役时是保留数据重新安装。保留数据不等于原配置已通过新版本检查，安装完成不等于业务就绪。未知结果只核对原操作，不能另造操作重复安装。原版本残留仍沿原固定清单处理。

## 实施和验收

实施中；完成后填写实际命令、源码、测试结果和交付身份。当前不将先前单次准备/取消冒烟扩张为完整安装生命周期已验证。

测试使用唯一合成产品 ID 和 D 盘受保护子目录，Windows 注册和文件操作只影响该隔离产品；真实 sportmaster 登记、管理凭据和数据原件保留。没有 SDK/设备控制；实际产品安装及目标工控机/现场资格单独记录。

Windows 机制依据：[受限令牌](https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens)、[CreateProcessAsUser](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw)。这是程序以更低权限运行，不能取代原运行和维护守卫。
