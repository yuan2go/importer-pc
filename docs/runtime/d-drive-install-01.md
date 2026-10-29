# D 盘安装预检与服务登记诊断

2026-10-29，工作包 [D-DRIVE-INSTALL-01](../work-packages/WP-IPC-D-DRIVE-INSTALL-01.md)，基线 main `f870ffc0`。用户同机同包观察到 D/D 预检失败、C 程序/D 数据在授权服务阶段报 1783、全 C 成功。三个现象按原阶段分别保留，尚无目标机原调用栈，不将不同失败合并为单一已证原因。

## 已证缺口与修改

- 原数据预检只证明运行账户可写。真实目录显式拒绝 SYSTEM 时仍通过；该负向验证先失败，补修后在任何安装写入前拒绝，并保留原 ACL。现检查 SYSTEM/其适用主体的目录访问、本地固定卷、持久 ACL、路径映射、文件占位、链接及 EFS 属性。静态权限检查不冒充实际 SYSTEM 进程写入。
- 原程序预检拒绝全部缺失的非默认父目录。当前对受保护祖先下的全新路径只读核验，正式授权后逐级建立显式 ACL；已有父目录不受保护仍拒绝并指出具体路径。不会把已有 D 盘目录或卷根改成所有人可写。
- 原服务 OpenService 的所有失败被当成“不存在”。现在仅 1060 进入新建分支；权限/RPC 等错误保留原 API 和代码。QueryServiceConfig 的首次缓冲区查询只接受正常的 122，其他失败不再被改写成长度异常。服务启动失败显示服务退出代码。
- 服务物理数据路径解析、身份解析、文件准备、SCM 打开、旧登记读取、新建、配置、恢复策略、启动及通道核对分别记录开始/完成/失败。路径解析还区分 CreateFileW 与 GetFinalPathNameByHandleW，避免 D 盘路径在打开 SCM 前失败却无阶段证据。原生 API、十进制/十六进制错误码、服务/数据/来源/宿主路径、脱敏调用栈和恢复失败分别保留。诊断写入故障不阻断恢复原服务，也不取代原异常。
- 固定安装操作回执保留结构化 `Error`；相邻 `<原操作文件>.service.jsonl` 保存受保护、逐条刷盘、有预算的阶段日志。回执保存也失败时转为结果待核对，禁止把丢失证据当成可重装。没有密码、授权票据、SDK 字节或业务报文日志。

程序目录、数据目录和 HKLM 管理的机级根仍是不同概念。选择 D 盘数据不自动迁移机器政策或凭据。普通安装/修复仍保留原数据根，现有 C 盘数据应沿当前完整备份、隔离恢复及原根退役流程处理；本包未执行迁移或真实产品替换。原请求、CAS、Unknown、管理权限及业务能力守卫不变。

## 已执行与未执行

原失败及本轮文本证据见 [证据目录](evidence/d-drive-install-01/README.md)。基线和修改后 Application 构建通过；5 项针对预检/诊断行为检查通过；原安装选项、8 项安装变更守卫回归通过；Sportmaster Desktop 构建通过。原失败不删除，不把注入的 1783 作为现场复现。

真实 SCM 的 C 程序/D 数据和全 D 两组验证已实现，但本会话 Windows 管理授权未取得：桌面交接无可信 helper，标准 runas 返回“操作已被用户取消”，管理员探针未执行。两组均 **NOT_RUN**，不能声明修复了目标机 1783 或已验证安装成功。没有更改 UAC、存储根、当前 sportmaster 登记/服务/密码/C 盘业务数据，没有连接设备。

隔离验证使用唯一合成产品 ID、全新目录和真实 SCM/System 服务；覆盖只读预检、新装、同源重装、实际普通令牌读程序/拒绝写程序/在 D 写图片样本、原密码保留及注入失败后的原服务恢复。删除仅限自身固定发布清单，保留 D 盘数据/证据；原政策/真实部署/服务命令和信任根前后核对，遇并行修改拒绝覆盖。桌面文件为合成输入，不称为完整生产安装 EXE 或真实图片采集验收。机级根沿本机已有配置，不为测试切换全机存储根；其实际位置写入回执。

管理员会话中的复验入口（先构建同源验证程序与自包含服务宿主）：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1
dotnet publish src/Ipc.Assembly -c Release -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.win-x64.lock.json -r win-x64 --self-contained true -p:RestoreLockedMode=true -o D:\InstallationCheck\service-app -m:1
./scripts/verify-installation-storage.ps1 -SourceApp D:\InstallationCheck\service-app -Output D:\InstallationCheck\new-matrix-evidence
```

脚本先检查真实管理员权限、全新 D 盘输出和 Platform 字节同源，不自动请求/绕过授权。也可提供同源自包含验证 EXE 的 `-VerificationExecutable`。普通构建与 RID 发布的编译条件可能不同，字节校验失败时不能强行放行，应使用同一发布闭包的验证程序。

目标机可用 `scripts/diagnose-installation.ps1` 只读采集卷属性、ACL、服务-数据根锚点、原安装回执及系统事件，报告仅写 D 盘。不要手工改存储根、删凭据、移动活动数据库或清除未决请求。未来生产存图、缓存、备份的实际位置和容量需独立验证，本包图片样本仅证明文件访问。
