# 当前版本首次运行

本项目为尚未对外部署的第一版。只支持当前版本从零安装、从零建库；任何非当前格式均拒绝，请使用全新数据目录。不得把旧安装、配置、凭据、草稿或数据库交给当前程序接管。当前身份与校验见[版本规则](version-compatibility-01.md)，此前指南原件见[归档](../archive/pre-fresh-baseline-01/README.md)。

## 安装和首配

Windows使用当前同源原生安装包，选择程序目录、全新数据目录及快捷方式，按向导完成Windows授权。程序、运行数据和项目缓存使用D盘；受保护根由安装管理员配置，不能用链接绕过ACL。指定账户以普通权限运行。详细安装、同格式覆盖安装、同版修复和卸载归属见[Windows交付](windows-application-delivery.md)及[生命周期操作](lifecycle-ux-current-guidance-01.md)。

安装后通过运行入口完成独立首次配置，使用管理模式设置密码，无默认密码。操作员身份是独立业务能力。四类参数编辑同一个完整候选，保存不等于生效；资源成功后才确认Effective。未使用相机IP/序列号均留空，RCS空地址表示待配置；部分配置拒绝，缺能力时拒绝相应业务。SM不提供RCS“稍后配置”勾选框。Sportmaster只使用supply-station；DirectPlc/WcsToPlc使用各自当前Shell。界面遵守[作业V2](../ui/sportmaster-task-redesign-v2-20261020/README.md)和[参数V3](../ui/parameter-settings-v3-20261020/README.md)。

## 源码和隔离模拟

Windows命令先执行 `. ./scripts/project-environment.ps1`。SDK由global.json固定；依赖按锁文件还原。源码选择和构建见[Application指南](application-architecture-22.md)。发布目录先核对SHA256SUMS、ASSEMBLY.json、SourceCommit及业务身份；源码运行不能当作发布包验收。

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
# 解压的当前portable候选，数据目录必须新建且隔离
dotnet D:/new-release/app/Ipc.Assembly.dll --config D:/new-release/app/config --data-root D:/new-isolated-runtime
```

Console输入 `snapshot` 读取Revision、Positions、Inputs、Configuration和容量；业务命令使用唯一Id及刚读取的ExpectedRevision。Sportmaster左右位分别使用left/right；PLC业务使用main。模拟输入仅代表明确合成实体。输入 `quit` 后等待Stop/Dispose完成。Supply使用Ipc.Product/StationConfig及[独立运行指南](operations.md)，Inspection保持独立参考装配。

## 当前状态和恢复

配置维护先进入管理模式，SM由Application临时阻断新准入并有界等待在途，不要求操作员先暂停；超时保留原配置。完整候选按原Revision/CAS保存Pending，再受控重启。当前Pending启动失败可回退已确认Effective；草稿冲突不覆盖原输入，失败修复保留当前原账。保存、识别、发送、接受和物理完成分别记录。

Unknown保留原请求、原报文、目标及独立接收责任，不自动重发。相同条码不代表同一实体。完整备份与恢复只处理当前格式，恢复到全新隔离目录；源退役、身份、范围和实物核对仍是启用前提。原始回包缺失不能用查询成功冒充已收到。软件暂停不是安全急停。

Windows、真实SDK/RCS、目标工控机和现场资格分别记录；合成测试不授予Production。历史PASS仅属于其原SourceCommit，当前工具不重验历史介质。
