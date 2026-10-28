# 2026-10-29 SDK整合验证记录

范围：[PR137](https://github.com/yuan2go/importer-pc/pull/137)，原头 `76dd6078faecbc2e120e6ca8dde222e7fcfc1398`，整合基线main `2bcde8bb063d97dc2907990616426d5e07c9c2c1`。本记录是该组合及下述修正的本机验证，不是所有任务的最终main验收。精确合并提交、CI和任务归宿由[整合台账](../work-packages/WP-IPC-INTEGRATION-20261028.md)及PR记录承接。

## 任务取舍与实现

用户确定只保留已走通的SDK相机路线。沿用MVS采图、进程内ZXingCpp及唯一Application/RCS原请求链，不合入PR135的新TCP生产者或PR136的独立服务/VM SDK。已有其他模块及历史驱动不借此改写合同。PR134的外部触发实体方案不替代当前连续读码需求。

普通启动逐处采用已合并PR139的实现；`ParcelApp.xaml.cs`与`OrdinaryRuntimeProcess.cs`的Git blob和上述main一致，保留原安装、身份、恢复子进程及监督器责任。两个workflow同时保留PR133边界回归、PR139隔离安装闭环及PR137新读码测试。

| 本次修正 | 实现与验证入口 |
|---|---|
| 解码器存在但未填写entity.mode时仍走observe-only | `MvsAcquisition`按有无解码器分别默认read-result/observe-only；显式observe-only仍保留。`MvsVerification.SoftwareBoundaries`分别核验两种结果。 |
| 停止时同步解码刚返回的非空结果被静默丢弃 | 先构造完整Undelivered，再检查取消并明确故障；原实例拒绝重启，无补发。可控解码阻塞验证原生帧已归还、停止仍等待实际解码、原结果和设备/epoch/来源不变、资源仅释放一次。仅为进程内保留，不宣称跨进程持久恢复。 |
| 长度大于Width×Height的Mono8帧被当作紧密图截断 | 在解码和计数前要求精确紧密布局；未确认stride的填充帧拒绝并归还。纯采集模块保留原负载合同。此处承接PR136 `MvsCaptureSession.cs`的严格布局边界，未引入其服务架构。 |
| WT049旧连接转换前看不到共用相机编码 | 承接外部保全补丁中的四项源码/测试改动；复用同一编码控件与候选，转换前后均恰有一个入口，左右相机隔离。另附的真机叙述仅归档，未转记PASS。 |
| 旧参数回归仍使用IDMVS扩展与已删除的相机状态区 | 回归夹具读取当前MVS/RCS能力目录及实际实例路径，保留尺寸、地址校验、独立编辑、保存及草稿断言。相机诊断走作业页真实“本位诊断”按钮，再返回原缓存表单。未恢复用户已删除的控件。 |

## 本机实际运行

所有开发命令先执行 `. ./scripts/project-environment.ps1`。完整命令、退出码、失败与成功原日志保全在仓库外 `D:/integration-backups/20261028-execution`。表中的PASS行为日志行数；不同套件可能重复覆盖同一行为，不相加冒充独立用例数。

| 命令/范围 | 结果及层级 |
|---|---|
| `dotnet build ImporterPc.slnx -c Release -p:Application=Sportmaster -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false` | Windows本机成功，0警告、0错误；非安装介质发布。 |
| SDK Probe `--self-test` | SELF_TEST 51、MVS 36、HARDENING 16项检查全部通过；原生接口为合成端口。三项新增边界均保留先失败、后修复的回执。 |
| Application `--mvs-software-flow` | 11 PASS行；实际ZXingCpp＋合成帧/SDK端口＋Application/SQLite/环回HTTP。三帧同码三份原请求、空白无请求、持久输入拒绝、设备设置拒绝/读回不符及有序资源释放。 |
| `--mvreader-flow`、`--hikrobot-sdk-editing`、`--hikrobot-hardening` | 分别6、11、14 PASS行；各守原接口边界。 |
| `--station-single-camera`、`--deferred-connections`、`--p0-deferred` | 分别52、130、58 PASS行，合成接口。 |
| `--rcs-timeout-errors`、`--p0-unknown-restart` | 分别57、3 PASS行；超时、错误、重启Unknown不盲重发。 |
| Application无参数、Supply `--current-contracts`及`--query-fixture` | 分别80、16、10 PASS行；当前合同和查询夹具，不代表本机全量Supply默认验证完成。 |
| `--ordinary-runtime-startup` | 实际Windows普通子进程4 PASS行，未改变本机UAC、HKLM或正式部署。 |
| 独立审查、生命周期模拟 | 20/20、7/7，无失败/跳过；合成原生端口，不是设备试验。 |
| 原生WPF `--startup-experience`、`--parameter-v3`、`--hikrobot-sdk-editing`、`--task-redesign` | 分别235、259、25、147 PASS行，均0 FAIL；实际HWND/Dispatcher，合成运行时与配置。当前桌面125%缩放，不扩大为目标IPC所有DPI或触摸验收。 |
| Python `test_mvs_runtime.py`、`test_mvreader_runtime.py`、`test_application_compatibility.py`、`test_rcs_local_simulator.py` | 分别10、3、16、1项全部成功。MVS打包断言更新到实际引擎标识，BusinessReady仍必须false；旧期望失败日志保留。 |

首轮首配窗口未在原4秒等待内出现编辑器；失败回执保留并增加失败状态/截图诊断，随后保持原断言复测通过，包括完整解决方案重建后的复测。没有证据将该单次等待失败归因于业务代码，也未扩大等待时间或删除断言。参数页的旧目录、旧字段路径、已删除入口断言失败分别保留，与三项SDK功能缺口分开记录。

本轮未对真实相机、真实RCS、正式部署身份或新冻结介质运行验收；PR137原分支记载的51/21次设备联调保留原来源，不能转记到本组合。未纳入厂商SDK、真实图像、条码或现场账本，未发布安装介质。
