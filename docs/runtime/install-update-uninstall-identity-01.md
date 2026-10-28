# 安装、更新、卸载与普通启动 · INSTALL-UPDATE-UNINSTALL-IDENTITY-01

2026-10-28，承接 PR138 卸载结果修复和并行 PR137 的普通令牌启动实现。用户本次异常发生在运行实例创建前；不是数据库、相机或 RCS 已启动后失败。

## 当前纠偏

“从开始菜单正常启动”不足以保证普通权限：安装器、文件管理器或桌面本身可能带提升令牌。日常桌面入口需要主动核对并在必要时创建同一用户的 Windows NORMALUSER 子进程，随后仍执行既有指定账户、完整性、配置、租约和业务守卫。安装、更新、卸载和管理服务的授权边界继续有效；不改全机 UAC、用户账号或现有 ACL，不通过模拟工厂绕过 Production。

四种安装目的由受保护原登记和 SourceCommit 判定。再次运行同源包是修复安装；不同源码是更新；原安装已退役时是保留数据重新安装。保留数据不等于原配置已通过新版本检查，安装完成不等于业务就绪。未知结果只核对原操作，不能另造操作重复安装。原版本残留仍沿原固定清单处理。

## 实施和验收

实现已完成。安装器的操作目的显示在滚动区域上方；完成后的打开失败保留已完成状态，可重试打开或取消勾选后关闭，不会再次安装。安装登记提交后的入口修复持有同一部署租约，核对原请求及 CAS，归档原失败回执的精确字节，成功后完成原回执并清理有证据的上一部署快捷方式。修复失败及责任未知仍保留原操作，不重交安装。已批准且核验一致的程序根不重复改写机器策略。

本机验证在 Windows 23H2 x64 / 125% DPI、UAC 关闭且调用进程为管理员的实际环境运行。命令均先执行 `. ./scripts/project-environment.ps1`，D 盘临时、构建及缓存；日志在 `D:/DevCache/importer-pc/verification/install-update-uninstall-identity-01`。工作包内公开证据只含代码和合成验证。

- Application 验证入口 `--ordinary-runtime-startup`：真实同用户 NORMALUSER 子进程、原运行守卫、参数往返及不递归转发。
- 内核验证入口 `--installed-lifecycle-windows`：唯一合成产品的真实受保护程序/数据目录、Windows ARP、Unicode COM 快捷方式、安装→同源修复→不同源更新→提交前失败→提交后修复→卸载→保留数据重装；每个已安装阶段启动真实普通权限子进程，运行原机器身份检查，证明可读程序、可写数据且不能修改程序。错误请求、失败修复和重复完成修复均保持原责任。
- 原入口 `--runtime-identity`、`--windows-runtime-setup`、`--installer-change-guards`、`--lifecycle-ux`、`--p0-supervisor` 回归通过；包含真实 SQLite 备份/Unknown 拒绝和监督恢复责任。
- 原生 WPF `--installer-lifecycle` 通过；安装目的和完成重试使用真实 HWND/Dispatcher，部署与启动回调为合成。最终同源冻结介质、首配/卸载按钮、管理服务与单 EXE 冒烟的独立回执随后补入本包证据，不能用开发构建代替冻结交付验证。

验证过程中保留了合成介质缺少配置、反射方法重名、误选默认 Supply 桌面构建等原失败日志；修复的是验证输入/定位，未删业务断言或放宽运行保护。

测试使用唯一合成产品 ID 和 D 盘受保护子目录，Windows 注册和文件操作只影响该隔离产品；真实 sportmaster 登记、管理凭据和数据原件保留。没有 SDK/设备控制；实际产品安装及目标工控机/现场资格单独记录。

Windows 机制依据：[受限令牌](https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens)、[CreateProcessAsUser](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw)。这是程序以更低权限运行，不能取代原运行和维护守卫。
