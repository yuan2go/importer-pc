# SM SDK→Python RCS、普通启动与参数续验（2026-10-29）

产品验证来源为main `7af0be617963a3a88319cb83fdf44fffd9440044`。只处理SM与必需共享基座，其他业务暂停；独立HTML导出工作树排除。身份、图像、实际条码、账号与原始账本只留D盘Git外。旧分支51/21笔记录保持原来源。

## 真实相机闭环

最初SDK枚举0；用户接通后重新枚举1台，IP及序列号摘要匹配此前明确配置的私有目标。使用此前获授权的离线MVS原生运行时，18项导出与版本0x04080201核验通过。没有修改网卡、系统策略或产品配置。raw probe的decoder=Unavailable是采集探针状态，不代表软件解码不可用。

干净main的既有验证入口使用实际MvsApi、实际ZXingCpp、SM Application/SQLite和本机Python HTTP服务运行20秒，退出0：

| 对账项 | 结果 |
|---|---|
| 客户端作业 / 冻结请求 / Python取格口请求 | 31 / 31 / 31 |
| 不同数字requestId | 31 |
| 客户端状态 | 全部SlotResolved |
| 请求原文、UTF8 SHA、原码、quantity=1 | 两端逐笔一致 |
| HTTP原始响应及SHA、requestId、taskId、格口 | 两端逐笔一致 |
| 每项发送尝试 | 均为1 |
| 同码 / Python分配 | 1种码、31个独立请求、1个分配 |

SlotResolved仅表示接受模拟格口，不代表物理包裹数量或执行完成。客户端不合并同码请求，由Python模拟器去重分配。

**层级：真实相机＋原生SDK＋软件解码；隔离Simulation身份前置接口；本机Python模拟RCS。不是正式部署或现场RCS验收。** 未使用正式受保护随包默认工厂路径。原件目录 `D:/ImporterPc.LocalValidation/SM-E2E-20261029-7af0be61/`，索引 `LIVE-MAIN7AF-RESULT.json`、`LIVE-MAIN7AF-CROSSCHECK.json`、`SDK-ENUMERATION-AFTER-CONNECTED.json`。真实报文、图像和身份不提交Git。

## 验证入口修复

旧MvsSoftwareFlow合成路径传入外部Python地址后，服务收到3个成功请求，但验证代码访问空的内部peer，在原第88行空引用、非零退出。这是验证入口缺陷，不能据此归因于#133产品ABI代码，也不能把三笔成功直接记成整套PASS。失败日志保留。初次候选读取操作员脱敏投影也失败，随后复用既有隔离夹具的管理模式授权读取原件，没有放宽生产脱敏或身份守卫。

只修改验证、脚本、CI与文档，未改生产业务、超时、配置或SDK。保留内部peer路径；外部合成路径要求显式Python SQLite账本，只读核对数量、请求ID、原文/哈希/原码/数量、一次发送、接受状态及HTTP响应原件。空帧无新增请求；SDK缓冲归还、拒绝盲重启和有序停止的原断言保留。

新增MvsPythonLedger对独立副本注入漏单、变更哈希、响应ID错配、重复记录，4种破坏均须被拒绝，之后再次核对原账本。不修改运行中Python账本或真机账本。

可复现入口：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
python scripts/verify-sm-sdk-python.py --data-root D:/ImporterPc.LocalValidation/NEW-SM-SDK-PYTHON-RUN
```

目录必须为尚不存在的绝对路径。脚本只启动/回收自己的loopback Python子进程，保留失败日志、合成账本及RESULT.json；核对加载程序集提交身份并标记WorkingTreeDirty。合成SDK帧使用实际软件解码和HTTP/Application/SQLite，不能替代真机。CI产物为合成数据。

候选脏树本机：外部Python17条PASS、3个不同请求ID、1个分配；内部peer11条PASS；Python协议单元测试1项通过；SM RCS156条、超时/错误57条PASS，均退出0。WorkingTreeDirty=true明确保留；提交后验证与CI保持各自来源，不追写到本页产品main基线。

## 普通启动与参数

以下均为上述干净main当前源码构建，退出0。数字为日志PASS断言行，不等于现场用例数：

| 入口 | 结果 |
|---|---|
| Application --ordinary-runtime-startup | 4；同SID普通令牌、管理员组停用、Unicode/引号/空参数保真 |
| Application --hikrobot-sdk-editing | 11；当前SDK参数候选 |
| Application --parameter-guidance-startup | 23；当前用途、首配与延后守卫 |
| Application --parameter-inheritance | 43；参数继承及既有合同 |
| 原生WPF --startup-experience | 235；首配、配置检查、模拟安装/卸载状态 |
| 原生WPF --parameter-v3 | 259；四类参数、同候选、保存核对和失败状态 |
| 原生WPF --hikrobot-sdk-editing | 25；内置MVS软件读码、左右身份、首配/日常入口 |

原生入口先构建tests/Ipc.Desktop.UiVerification（Release、BusinessModule=Sportmaster、RestoreLockedMode=true），IPC_FROZEN_DESKTOP指向同源Ipc.Desktop.dll，再执行对应DLL、工作区和选择器。计数取WINDOWS-WPF.log，无控制台输出不等于零项。已实际查看原生首配RCS页及日常左相机截图；示例为合成，不将模拟连接显示当真实在线。

SDK Probe按workflow使用IpcContractPath构建。早期DLL路径错误、缺该显式参数和长路径复制失败保留为工具准备错误；成功重建/运行另记，不归因于相机功能。

## 相机重点续测

按用户最新要求，RCS仅保留上述已通过的本机接口，不继续扩展接口覆盖。随后在同一干净main上专测相机连续采集和停止后重开，两次重开使用独立的Simulation客户端数据目录；不据此宣称同一正式部署跨重启资格。

| 运行 | 实际帧 / 空帧 / 识别输入 | 停止后持久账本 | 原视频采样间隔计数 |
|---|---|---|---|
| 连续120秒 | 161 / 39 / 122 | 119个SlotResolved；2次多码拒绝；1次时效拒绝 | 1624 |
| 停止后重开1，10秒 | 17 / 6 / 11 | 11个SlotResolved | 125 |
| 停止后重开2，10秒 | 17 / 3 / 14 | 13个SlotResolved；1次多码拒绝 | 117 |

三轮退出0，SDK故障字符串为空、Unknown为0；退出后可再次独占打开同一设备，未发现本轮资源占用阻断。120秒的结束瞬时快照为118个SlotResolved加1个ResolvingWcs，停止后的SQLite确认119个均SlotResolved，不能把瞬时快照当最终账本。

这不是零拒绝或全帧解码通过：原视频采样间隔明确计数，不能解释为已识别输入丢失。已持久识别输入分别为122/11/14，均Processed，准入与拒绝数量逐项对应。每轮客户端数据目录独立，不合并宣称跨轮requestId唯一；RCS仍只是同一个本机模拟接收端。

发现但未处理：

- 120秒一笔采图至入队1569.320ms，超过夹具现有1500ms输入时效，按守卫拒绝。该轮最小282.255ms、中位465.011ms。未调大门限、未挪动ObservedAt伪装新鲜、未放宽准入；需进一步分析6MP全图软件解码及调度耗时，当前不声明最坏时延达标。
- 三帧各有两个内容相同的解码候选，当前合同按MultipleCodes拒绝。相同内容不证明同一个物理码；未擅自去重候选、挑第一个码或重定义实体。是否调整同帧候选语义须单独明确；不同图像中的独立识别继续分别上报。
- 本次画面未进行受控的移码、遮挡、旋转、运动速度及多种码制样本测试；空帧计数不等于这些实物场景已验收。

补做不经解码/RCS的15秒原始采集对照：当前main既有 `Ipc.Drivers.Hikrobot.Probe --mvs-connect` 实际退出1，取得14帧后报 `MVS frame sequence gap/reset`。这是原始诊断的严格连续帧门禁，不是上述三轮软件读码的运行结果；软件读码路线允许并显式统计原视频采样间隔。对照未取得有效吞吐基准，间隔原因未定位，不能判为相机硬件故障或接口全绿。未改守卫、SDK缓冲策略或采集参数。失败原件 `camera-focus-main7af/raw-acquisition-15s-private.log` 及 `RAW-ACQUISITION-RESULT.json` 保留Git外，公开仅附无身份的结果摘要。

三轮摘要见[不含设备身份的真机汇总](evidence/sm-sdk-camera-20261029/summary.json)。私有原件在前述目录的camera-focus-main7af/，含RESULT.json、PERSISTED-CAMERA-RESULT.json、CAMERA-LATENCY-AUDIT.json和CAMERA-MULTI-CANDIDATE-AUDIT.json。短时进程采样不是资源无泄漏或24h/72h资格证据。

## 未验证

- 当前main正式安装/更新后普通用户完整启动；本机既有安装进程来源较旧，本轮未替换或发布介质。
- 当前main正式受保护随包默认工厂、默认身份守卫下的真机完整路径；隔离Simulation实采不替代该项。
- 现场真实RCS、现场网络/权限合同、物理执行与完成。
- 真机拔线、断电、重启恢复、24h/72h长稳、目标工控机和现场验收。
- 其他业务继续暂停；历史非SM夹具失败不标为已修复。

未改冻结历史、旧RV、安装包SourceCommit或现场验收。整合归宿见[执行记录](integration-20261028.md)。
