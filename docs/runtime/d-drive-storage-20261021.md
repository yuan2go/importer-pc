# D盘开发与新站存储

SM 新站默认程序 `D:\CainiaoSupplyStation`、数据 `D:\ProgramData\ImporterPc\data`；既有部署继续使用原登记路径，不因默认值调整迁移数据。默认目录不取代原权限预检，详见 [D-DRIVE-DEFAULTS-02](d-drive-defaults-02.md)。

D 盘安装预检和服务登记日志补修见 [D-DRIVE-INSTALL-01](d-drive-install-01.md)。受保护祖先下的缺失程序父目录可在授权后逐级创建；已有不受保护目录拒绝并指出位置，不自动放宽 ACL。数据预检包括 SYSTEM 服务访问条件。两组真实安装矩阵尚待 Windows 管理授权，不能据此宣布目标机安装通过；当前数据不自动迁移。

项目文件、开发缓存、安装程序和运行数据使用D盘。当前只从零安装、从零建库，不提供C盘旧模拟目录接管。原迁移过程和验证结果完整保存在[指南原件](../archive/pre-fresh-baseline-01/README.md)及原证据中；不作为当前安装步骤。

Windows开发命令先执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj
```

脚本仅设置当前进程的TEMP/TMP、dotnet用户目录、NuGet包/HTTP缓存和UTF-8；不更改全机环境。D:/DevCache/importer-pc和D:/DevCache/nuget属于开发缓存，不是业务数据。

安装向导允许选择程序和全新数据位置，保留D盘偏好。受保护机器存储根使用64位HKLM/Software/ImporterPc/Storage的Root，由Windows安装管理员管理；运行端验证注册表/目录归属、ACL、本地固定卷及祖先权限，不接受环境变量或链接绕过保护。日常账户、管理模式、机器身份和退役围栏保持。

管理密码无默认值，首次通过当前设密流程配置；操作员身份独立。不要手工移动机级身份或目录、替换密文或复制运行数据来接管旧站。需要保留的当前数据使用完整备份恢复到新隔离目录，并完成来源退役与身份核验。

本次开发不会触碰本机安装或运行目录。用户清理旧开发站点时先停止应用，使用系统“已安装的应用”中的归属卸载入口；确认运行任务/服务已停止，再核对该站登记的数据和备份目录并人工删除可放弃内容。不要删除整个D盘根、共享DevCache、其他站点或海康SDK。若卸载失败，保存错误与登记信息，使用原安装器修复/卸载，不手改HKLM或服务所有权。
