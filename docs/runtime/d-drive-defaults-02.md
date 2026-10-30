# SM 新站 D 盘默认安装位置

2026-10-30，[工作包](../work-packages/WP-IPC-D-DRIVE-DEFAULTS-02.md)，基线 main `d899eeac`。

全新 SM 安装默认程序目录 `D:\CainiaoSupplyStation`，数据目录 `D:\ProgramData\ImporterPc\data`。原有部署优先显示原登记路径，用户改选及已审阅路径优先于默认值；更新安装包不会自动迁移 C 盘数据。D 盘不可用或不满足原权限/本地固定卷要求时明确预检失败，不回退 C 盘。

本机此次失败的实际错误为 `D:\Program Files` 不满足维护所有者要求；该目录由普通账户持有并允许普通用户修改。独立 D 盘程序目录已在原 `d899eeac` 包通过只读预检。这里记录的是预检原因，不将其与早前目标机服务登记 1783 合并为同一原因。原 [D-DRIVE-INSTALL-01](d-drive-install-01.md) 证据保留原身份。

程序、业务数据与 HKLM 管理的机级政策/凭据根仍分别管理。默认值只决定新站程序和业务数据位置；不手改机级登记、移动凭据或清除既有安装。

## 交付与已执行验证

产品与包源 `00d74679d25d60c4fc7991f81cd266af9c83bc2c`，产品路径修改在 `bd7e581e`；后一提交仅同步随包启动说明。自包含 x64 安装 EXE 位于本机 `D:/deliveries/cainiao-d-defaults-00d74679/installer/CainiaoSupplyStation_SM_Setup_v1.0.0_win-x64.exe`，SHA256 `ad97a1247926b3ca12e94d4f6da3efa30c941dd722c4da5496e834a6fc2819ca`。只留本机，不上传二进制/SDK。

- UiVerification、Application.Verification 构建通过，均 0 警告/0 错误。
- 原生 WPF 安装流程回归通过：新站两个 D 盘默认值、既有 C/D 程序/数据根保留、用户输入及失败重审保留、原 Unknown 防重行为。使用真实 HWND/Dispatcher 与合成安装契约，不冒充真实安装。
- 5 项存储/日志行为检查、8 项安装变更守卫和 3 项发布文档测试通过。原始合成日志见 [证据目录](evidence/d-drive-defaults-02/README.md)。
- 同源介质/EXE 构建通过，安装器和媒介共享 DLL 字节核对通过；792 个媒介文件的清单/哈希及 ZIP CRC 核对通过。真实 EXE 已启动并完成准备，识别当前 D 盘部署为更新并保留原路径；只读关闭，未提交现有产品更新。
- 已读取旧 `d899eeac` 安装的 Completed 回执、Installed 登记及 Running 服务；旧包安装成功不转记为新包通过。

## 真实系统安装边界

全 C/D、D/D 多轮矩阵已取得管理员权限，但 C 盘可用约 284 MiB，预算预检在任何安装前拒绝；没有删除 C 盘原文件换取空间。原矩阵仍未完成。

补充 `-FreshDOnly` 入口，只运行 D 程序/D 数据全新安装及普通用户文件访问，保留 64 MiB 余量；机级服务宿主沿既有政策，本机仍在 C 盘。回执显式标明 FreshInstallOnly，重装/失败恢复 NOT_RUN，不能冒充完整矩阵。测试仍复用真实安装内核与 SCM/System 服务，使用唯一合成产品 ID、独立目录、合成配置和图片样本；不是最终 EXE 对生产部署的完整安装。

实际运行还发现并修复旧夹具的两处假设：从完整发布目录复制了不属于合成种子的配置，以及将服务日志放在普通报告目录导致第二次追加拒绝。当前只复制同源二进制与依赖，使用明确审阅的合成种子；日志保存在本次已批准的受保护程序根。另修正 PowerShell 5.1 将单字符串 splat 成字符参数的问题，显式使用字符串数组，实际参数探针通过。

修正轮的新测试尚等待 Windows UAC 授权，真实 D/D 新装未取得最终 PASS；未验证最终 EXE 对全新机器的完整交互安装。前面失败轮已核对原 sportmaster 登记、服务命令、机器政策及可信根字节不变；无生产设备操作或当前数据迁移。失败回执与清理证据保留本机独立目录，不吞异常或改成成功。

复验入口：在已有管理员会话运行 `scripts/verify-installation-storage.ps1 -SourceApp <同源自包含app目录> -VerificationExecutable <同源验证EXE> -Output <全新D盘证据目录> -FreshDOnly`。省略最后开关仍执行原两种路径、重装和失败恢复全矩阵，先检查容量。
