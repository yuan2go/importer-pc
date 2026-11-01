# PR161 Windows 整合与 D/D 新装补证

2026-11-01，源码 `ff065a2ed634ca9f2409d71de8a543e33b027fac`，整合 main `ae3a380f`（PR163/164/165）。产品变更仍限新站路径选择；已有部署、用户输入及审阅路径保持优先，不迁移现有数据或机级根。修改前索引和原报告见[原字节归档](../archive/pre-pr161-windows-20261101/manifest.json)。

冲突只有 INDEX、保真属性及 manifest。保留各PR当前入口和历史原件，使用既有脚本从实际暂存Git字节重建清单，未覆盖并行业务代码。

SM桌面、UiVerification及Application.Verification构建均0警告/错误。实际HWND/WPF安装流程通过：SM全新默认程序 `D:\CainiaoSupplyStation` / 数据 `D:\ProgramData\ImporterPc\data`、既有C/D两根保留、用户输入/失败重审保留及Unknown防重；5项存储诊断、8项安装变更守卫和3项发布文档测试通过。

## 实际 D 程序 / D 数据隔离新装

Windows UAC授权后，以唯一合成产品、独立D盘程序/数据目录、同源自包含SM服务宿主执行FreshDOnly。真实受保护安装和SCM/System服务完成；普通用户实际读取程序、修改程序被拒绝、写入D盘图片样本并查询服务通过。新数据保留，服务日志无合成密码。服务停止后仅按精确所有权清单移除本轮程序/服务宿主，原宿主保留。现有SM登记、服务命令、机器政策和可信根字节在前后核对一致。根回执为PASS / FreshInstallOnly / OriginalInstallationUntouched=true。

发布宿主和验证程序集的Platform DLL逐字节相同，SHA256 `78dcea32d2e77e945d512d9e7ec2f8db74936b119703d855251c7a97fb8a3599`。验证宿主沿正常锁定构建，用D盘dotnet运行；系统服务使用同源自包含宿主。机级服务/凭据根服从管理员原有HKLM策略，不借本次默认值变更迁移或绕过权限。

这是共同安装内核＋真实SCM与普通用户权限的D/D新装验证。精确默认值由原生安装选项验证，隔离内核用独立合成目录，未对现有SM提交更新。完整C/D、D/D重装/失败恢复矩阵和最终安装EXE对全新机的完整交互安装仍NOT_RUN；原2026-10-30容量/UAC失败记录保留，不记为本轮通过。本轮按新增D盘默认值的风险取得代码整合依据，不宣布原完整安装任务或现场资格全部完成。

失败尝试保留：验证宿主的win-x64自包含发布因原普通验证锁没有该RID而NU1004；未改生产依赖或锁文件，改用正常锁定构建后Platform字节与发布宿主一致。首次管理员启动器引用本机不存在的常规pwsh路径，未启动进程；改用实际存在的Windows PowerShell后取得UAC授权并执行。安装真实登记/策略/凭据及二进制仅留D盘；公开仅合成断言输出、根范围结果和哈希。Windows PowerShell原始矩阵日志是UTF-16，按原字节保存，不转码。

本轮另保存PR163的Windows本地关闭/资源/原图/离线重连/窗口生命周期替代夹具与MVS87项通过日志；原被测产品/测试树与main `b154512a`一致。继承的acquisition-lifecycle旅程缺口保持原FAIL/TIMEOUT，不扩张为全套通过。

[摘要与原始合成日志](evidence/pr161-windows-20261101/summary.json)记录源码、各轮身份、范围及哈希。真实SDK、客户RCS、目标机/现场仍NOT_RUN；CI停用、D/RV不变。没有生成或发布新的完整安装EXE，不覆盖旧交付介质。
