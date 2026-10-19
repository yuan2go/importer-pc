> 当前增量以[UX审查WP-01～07闭环](../work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md)为准；继承PR37固定实现，旧阶段/候选记录保留。开工main仍1.2.8，PR37本地1.3.0未远端发布，本轮声明1.3.1/Contracts1.9.0/DB7。Windows实际NOT_RUN，真实组合/合同BLOCKED。

> 本分支按[UX修复工作包](../work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)更新现行操作；修改前说明的固定Git原字节见[索引](../archive/pre-ux-commercial-fix-01/README.md)。开发验证、最终媒介身份和Windows/现场验收分别记录，本文不宣告后者通过。

# 首次运行：选择已安装产品

Sportmaster、DirectPlc、WcsToPlc使用`Ipc.Assembly`及`ParcelConfiguration`；Supply使用`Ipc.Product`及`StationConfig`，入口和命令分别读取。软件暂停不是安全急停。Windows实际NOT_RUN，真实SDK/合同/现场BLOCKED。

Windows安装、普通账户、启动/快捷方式、升级中断/回退修复、保留数据卸载及备份/诊断的当前完整路径见[Windows交付指南](windows-application-delivery.md)。

## 三个Application

固定SDK10.0.402；源码构建与发布入口见[Application运行指南](application-architecture-22.md)。发布ZIP解压到新目录，先核对SHA256SUMS与ASSEMBLY.json的SourceCommit和ApplicationId。portable需要.NET10运行时；win-x64含实际WPF但仍需Windows验收。

```sh
dotnet /new/unpacked/app/Ipc.Assembly.dll --config /new/unpacked/app/config --data-root /new/isolated/runtime
```

输入`snapshot`查看健康、Positions、Inputs、Configuration及容量。Sportmaster默认左右独立reader；两种PLC业务只有main位。每行输入完整BusinessCommand，Id为唯一32位GUID，ExpectedRevision取刚读的应用Revision；模拟输入不表示真实实体检测：

```json
{"Id":"11111111111111111111111111111111","ExpectedRevision":0,"Kind":"EmitReferenceAt","Owner":"synthetic-parcel-1","Value":"{\"PositionId\":\"left\",\"Code\":\"000000917\"}"}
```

等待本位SlotResolved；Sportmaster不发PLC。最新输入的Admitted/Rejected/Blocked/Duplicate与作业主链分别读取；无码、多码或非法码不会创建新Operation，旧终结结果不能解释本次失败观察。旧未决请求及责任仍保留，不因新观察清除。PLC业务必须分别查看PlcAccepted和权威PhysicalCompleted。右位用right及新身份；其他业务用main。结束输入`quit`，等待完整Stop/Dispose。登录/重量/图片可选旅程、完整配置与设备接入见[Application指南](application-architecture-22.md)，故障恢复、容量、维护及扩展操作见[维护指南](architecture-quality-closure-01.md)和[扩展开发](../extensions/developer-guide.md)。

Parcel使用数据库7，SQL1–6原样继承，SQL7追加数字请求唯一归属/范围退役；RCS原报文/拆包语义继承SQL6；DB3/4/5历史根升级和资料责任按维护指南执行，不使用新seed覆盖旧账。旧配置文件未记录资源成功时，启动保留原JSON/hash并标记ConfirmationRequired；资源实际成功后仍暂停，先在维护页读取待确认原账、核对证据并确认，再显式Resume。CLI的ConfirmConfiguration使用读取时的`RuntimeId`、`Configuration.Revision`、`Confirmation.EffectiveHash`组成Owner=`runtimeIdN:configurationRevision:effectiveSha256`（runtimeIdN为无连字符32位GUID），Value为维护检查说明，ExpectedRevision为提交时应用Revision；任一身份/修订变化须重新检查，不自动改用最新内容确认。

配置维护先Pause，再AuthorizeMaintenance（Owner=maintenance、Value=SIMULATION_ONLY仅限参考认证）。schema3结构化草稿保留全部reader、投递位、每位能力绑定及未编辑策略；提供者从已安装精确身份选择，先做字段校验再做完整候选校验。加载时固定配置Revision，自动刷新不能推进草稿修订；高级JSON默认只读；通过“表单生成高级JSON并切换”或“将当前高级JSON加载到表单”显式切换同一草稿，不隐式互相覆盖。ValidateConfiguration只校验；StageConfiguration用加载时配置Revision、Owner为维护原因、Value为完整JSON，保存Pending。原件清偿后受控退出/重启，全部资源成功才确认Effective；中断保留旧Effective和候选，ActivationFailed须比较原尝试后显式重新保存。冲突保留草稿，重新加载比较，不自动覆盖。

## Supply兼容产品

仅Supply使用共享箱、Quantity、OpenContext/Submit及旧供包接收端。按[Supply首次作业](supply-getting-started.md)和[Supply运行维护](operations.md)执行。Inspection为独立参考装配，不套用新Parcel或Supply命令。历史源码/候选/PASS按原版本读取。
