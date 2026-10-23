> 2026-10-21 安装与首配最新增量见[当前操作说明](startup-experience-02.md)：中文用途、可选条码筛选、文件导入及未完成续配；保存配置与开始作业分开，保存不代表生效或启动。下文原版本及原验收保持身份。

> Sportmaster本轮缺省界面为supply-station，保留显式 `--ui classic`；展示设置提供布局/辅助窗/来源诊断，其他Application缺省不变。当前运行与本轮Windows合成验证见[视觉产品化](sportmaster-visual-productization-01.md)，原发行证据保持其SourceCommit。


> 现行商业化补差范围见[完整工作包](../work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md)及[基线核对](../reviews/commercial-productization-01-baseline.md)。本页保留稳定操作；商业化增量按上方当前产品指南读取。正常安装/首次启动/独立维护以[原生启动部署指南](startup-deployment-02.md)为准，旧媒介按其固定源码和随包说明使用；源码与介质身份见[发布记录](publication-deployment-20261019.md)。历史任务/测试/候选保持原身份，Windows/真实组合未验部分不变。
> COMM-00～10 当前操作见[产品操作指南](commercial-productization-01-operations.md)，实现与冻结验证见[实施结果](commercial-productization-01-results.md)。本页的历史版本/原验收不改写；当前边界仍受四层职责、唯一权威、指定账户和原请求守卫约束。

# 首次运行：选择已安装产品

Sportmaster、DirectPlc、WcsToPlc使用`Ipc.Assembly`及`ParcelConfiguration`；Supply使用`Ipc.Product`及`StationConfig`，入口和命令分别读取。软件暂停不是安全急停。下述早期Windows未验记录保持原范围；后续本机证据按索引逐版读取，不能代替真实SDK/合同/现场验收。

Windows安装、普通账户、启动/快捷方式、升级中断/回退修复、保留数据卸载及备份/诊断的当前完整路径见[Windows交付指南](windows-application-delivery.md)。

## 三个Application

Windows用户核对完整媒介后双击`Setup.cmd`，或首次双击`application.cmd`进入原生安装向导。选择运行账户、原数据根和配置来源，可选独立信任/离线扩展包；检查摘要后允许Windows授权。软件完成程序并排部署、机级策略、权限、新目录和快捷方式；无需手工JSON、hash或PowerShell。安装后使用开始菜单运行入口，既有数据/政策不覆盖。失败页和`Maintenance.cmd`可进入不启动设备的离线维护，表单保存Pending或从完整备份恢复到新目录，再重试正常启动。流程、种子修复和严格准入边界见[安装与启动修复指南](startup-deployment-02.md)。

固定SDK10.0.402；源码构建与发布入口见[Application运行指南](application-architecture-22.md)。发布ZIP解压到新目录，先核对SHA256SUMS与ASSEMBLY.json的SourceCommit和ApplicationId。portable需要.NET10运行时；win-x64含实际WPF但仍需Windows验收。

```sh
dotnet /new/unpacked/app/Ipc.Assembly.dll --config /new/unpacked/app/config --data-root /new/isolated/runtime
```

输入`snapshot`查看健康、Positions、Inputs、Configuration及容量。Sportmaster默认左右独立reader；两种PLC业务只有main位。每行输入完整BusinessCommand，Id为唯一32位GUID，ExpectedRevision取刚读的应用Revision；模拟输入不表示真实实体检测：

```json
{"Id":"11111111111111111111111111111111","ExpectedRevision":0,"Kind":"EmitReferenceAt","Owner":"synthetic-parcel-1","Value":"{\"PositionId\":\"left\",\"Code\":\"000000917\"}"}
```

等待本位SlotResolved；Sportmaster不发PLC。最新输入的Admitted/Rejected/Blocked/Duplicate与作业主链分别读取；无码、多码或非法码不会创建新Operation，旧终结结果不能解释本次失败观察。旧未决请求及责任仍保留，不因新观察清除。PLC业务必须分别查看PlcAccepted和权威PhysicalCompleted。右位用right及新身份；其他业务用main。结束输入`quit`，等待完整Stop/Dispose。登录/重量/图片可选旅程、完整配置与设备接入见[Application指南](application-architecture-22.md)，故障恢复、容量、维护及扩展操作见[维护指南](architecture-quality-closure-01.md)和[扩展开发](../extensions/developer-guide.md)。

Parcel使用数据库8，SQL1–7及checksum原样继承；SQL7追加数字请求唯一归属/范围退役，SQL8是首配修复、恢复交接和备份策略的读取者屏障；RCS原报文/拆包语义继承SQL6；DB3/4/5历史根升级和资料责任按维护指南执行，不使用新seed覆盖旧账。旧配置文件未记录资源成功时，启动保留原JSON/hash并标记ConfirmationRequired；资源实际成功后仍暂停，先在维护页读取待确认原账、核对证据并确认，再显式Resume。CLI的ConfirmConfiguration使用读取时的`RuntimeId`、`Configuration.Revision`、`Confirmation.EffectiveHash`组成Owner=`runtimeIdN:configurationRevision:effectiveSha256`（runtimeIdN为无连字符32位GUID），Value为维护检查说明，ExpectedRevision为提交时应用Revision；任一身份/修订变化须重新检查，不自动改用最新内容确认。

配置维护先进入管理模式（首次按真实设密/批准流程设置，无默认管理密码），再Pause并等待排空。AuthorizeMaintenance的历史SIMULATION_ONLY示例不再作为当前管理授权。schema3结构化草稿保留全部reader、投递位、每位能力绑定及未编辑策略；提供者从已安装精确身份选择，先做字段校验再做完整候选校验。加载时固定配置Revision，自动刷新不能推进草稿修订；高级JSON默认只读；通过“表单生成高级JSON并切换”或“将当前高级JSON加载到表单”显式切换同一草稿，不隐式互相覆盖。ValidateConfiguration只校验；StageConfiguration用加载时配置Revision、Owner为维护原因、Value为完整JSON，保存Pending。原件清偿后受控退出/重启，全部资源成功才确认Effective；中断保留旧Effective和候选，ActivationFailed须比较原尝试后显式重新保存。冲突保留草稿，重新加载比较，不自动覆盖。

## Supply兼容产品

仅Supply使用共享箱、Quantity、OpenContext/Submit及旧供包接收端。按[Supply首次作业](supply-getting-started.md)和[Supply运行维护](operations.md)执行。Inspection为独立参考装配，不套用新Parcel或Supply命令。历史源码/候选/PASS按原版本读取。

当前源码中Supply与Inspection直接运行入口同样在配置读取和持久资源创建前校验Windows机器策略与指定未提权账户（允许UAC deny-only组身份）；Inspection的Simulation限制仍保留。旧候选不自动更新，后验及维护权限边界见[全部入口门禁补齐](runtime-identity-preflight-02.md)。
