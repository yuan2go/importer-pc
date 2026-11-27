# 平台与业务模块的架构基线

Importer PC 使用 C#、.NET 10、WPF 和 SQLite。构建时由产品和装配根选定业务及其依赖，Windows 工作站持有运行中的业务实例。当前交付 SM；DirectPlc、WcsToPlc、Supply 和 Inspection 保留各自的已有装配。开发范围及持久化安全约束见[工程规则](../../AGENTS.md)。

| 职责 | 实现位置 | 谁作决定 |
| --- | --- | --- |
| 产品、构建与装配 | Ipc.Product、Ipc.Assembly | 选择 Application、端口实现和依赖闭包，构造宿主并绑定资源 |
| 平台与存储 | Ipc.Platform、Ipc.Platform.Storage | 提供机器与用户身份、受保护路径、部署登记、资源所有权、SQLite 写入及恢复机制 |
| 共享供包应用逻辑与具体业务 | Ipc.Application.Contracts、Ipc.Application.ParcelFlow、Ipc.Application.Sportmaster 等 | 应用声明拓扑、角色、准入规则、状态机和业务账，执行命令时核对权限与版本 |
| 设备、协议 | Ipc.Drivers.Hikrobot.Mvs、Ipc.Integrations.SportmasterRcs 等 | 驱动占用及释放真实设备，集成核对协议响应，向 Application 返回可归属的证据 |
| WPF 与运维 | Ipc.Presentation.Wpf、业务 Desktop、Ipc.Operations | 投影带身份和版本的状态，保存本窗草稿；独立运维进程经本机 IPC 访问工作站 |

## 现状与下一阶段的边界

PR 1 已把部署与基座层的产品能力收敛到 `ProductDeploymentCatalog`。编译期装配选定业务 ID，平台、启动安装壳和独立运维据此读取部署声明。运行时仍逐项核验受保护安装登记、介质、程序及数据所有权。声明只供执行时查询，原登记、发布清单和业务账继续使用既有格式与身份。未知产品不具备任何已声明能力。[产品部署声明](../../src/Ipc.Platform/ProductDeploymentProfile.cs) · [部署快捷方式](../../src/Ipc.Platform/DeploymentShortcuts.cs)

[ParcelCapabilityFactory](../../src/Ipc.Assembly/ParcelCapabilityFactory.cs) 仍绑定 SM 的内置能力。能力工厂、共享工作站字段和轮次契约按后续 PR 分别处理。[解耦清点](multi-business-extraction-audit.md)

通用平台层不得新增 `Id == "sportmaster"` 或等价的业务特判。业务决定能做什么，平台执行有明确输入、身份和失败语义的操作。具体例子是由应用判定本轮是否允许提交，由存储机制原子提交应用给出的身份及事实；由装配确定快捷方式用途，由部署机制核验实际安装、目标和所属用户。引入新的共享能力要有实际消费者，不以规划中的业务替换已有装配。

## 一条作业的所有权

1. 驱动提供带会话、次序与采集时间的设备事实。相机采到画面、识读出候选、确认托盘为空分别有独立依据。UI 看见预览不能生成一次业务提交。
2. Application 将输入与本位的放置、到位、作业及配置关联，取得一次提交资格，持久保存请求和原参数。设备或网络等待不能借公共锁占住另一工位的输入。具体边界由[轮次契约](sm-continuous-induction-closure-01.md)定义。
3. 集成按冻结身份执行一次外呼并保存原回包，业务决定能否接受该结果。调用中断、超时及回执落盘失败分别保留责任。新配置、新车、切页均不得使旧请求重新发送。
4. 工作站发布有界快照，UI 按当前身份显示并让操作员查询原记录。运维仅通过声明的观察和控制通道访问工作站，不能从 IPC 断线推定硬件释放或开启第二个业务所有者。[双进程契约](sm-readonly-operations.md)

## 配置与部署

系统、业务、驱动和组件从同一本配置账取得当前修订。保存完整候选并实施 CAS，按资源和业务责任决定可热生效或保留 Pending。恢复已接纳请求时按创建它的原始配置和请求执行。运行数据、配置修订、部署登记、个人偏好分别由所属存储维护。[当前格式](../runtime/version-compatibility-01.md) · [配置保存](../runtime/parameter-inheritance-02.md)

停机、安装、回滚、恢复和清理都要先核验实际所有权。复制或回滚程序不能替换业务库、撤销既有外部效果或覆盖永久编号。恢复已复制的隔离数据，仍须核对原站和原请求，按明确操作启用。[安装生命周期](industrial-lifecycle-01.md) · [正式与测试实例](sm-test-instance-isolation.md)
