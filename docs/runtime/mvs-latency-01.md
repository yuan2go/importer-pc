# MVS 时延调查（2026-10-29）

工作包：[MVS-LATENCY-01](../work-packages/WP-IPC-MVS-LATENCY-01.md)。基线 `f870ffc0`，旧真机基线 `7af0be61`。本轮确认两类独立开销：全图 ZXing 调用和取流线程内的周期设备枚举。已修复像素完全等价的对比度计算开销，补齐可复现诊断；未证明生产最坏时延达标。

## 版本可比性

`git diff 7af0be61 f870ffc0` 核对：`SoftwareDecoder.cs`、`IMonoBarcodeDecoder.cs`、`MvsApi.Acquire/Release/ConfigureBuffers` 没有算法或缓冲策略变化。3个SDK缓冲、OneByOne、紧密Mono8全帧复制、归还后同步解码、全码制/增强/旋转/反色/下采样搜索设置保持。Application Enqueue/ObserveAsync 与 ObservationSession 的输入时钟、序号守卫亦未改变。

#152 改了发现/打开失败、采集/健康异常、自动重连及 Stop/Close CALLORDER 后清理行为。Application启动新增重连任务和旧暂停恢复，可能影响调度；不能把旧性能数字当受控A/B。旧连续采集能用于分析相同的稳态源逻辑，旧停止重开不能授予 #152 真机拔插、新epoch或安装资格。#148修复验证入口并记录证据；#154收口没有更改上述稳态算法。

## 时钟与责任

| 阶段 | 实际行为/计时边界 |
|---|---|
| 曝光、传输、SDK排队 | 旧ObservedAt尚未建立，不在465/1569ms指标内；该数字不是实物到达时延 |
| SDK取帧 | 单次100ms等待，可能NoData；新增全部调用、有帧、无数据三组计时 |
| SDK返回→解码前 | 返回后建立原ObservedAt，验证尺寸/Mono8/完整性/帧号，租用托管缓冲并复制，再归还SDK借帧 |
| 解码 | 同一线程先对比度，再实际ZXing-C++，最后提取完整候选 |
| 解码→宿主 | 清零归还整个池数组，建立原事件/准入身份；不移动ObservedAt，不按码去重 |
| 同步交付 | ObservationSession复核epoch；Enqueue持有本位lane.Sync，建立ReceivedAt，SQLite持久Pending后入有界队列。true只表示接收了输入责任 |
| 排队/处理 | 独立消费者等待共享业务gate；已有parcel_ingress.queue_ms/processing_ms。时效/健康/实体/RCS守卫仍执行，入队不等于接受或物理完成 |
| 周期健康 | 约每1秒在取流线程Connected→全局枚举匹配原IP/序列号→触发读回→发布；期间不继续GetImageBuffer |

旧CaptureToIngress为原件中的 `ReceivedAt - ObservedAt`，**不含SQLite写入本身**，可能包含lane.Sync前的等待。新 `host.observed-to-receiver` 是调用宿主前单调时钟；`application.durable-handoff` 包括包装、lane等待、持久提交和入队，不能直接改名互换。Application排队/处理及RCS完成继续分别记账。

旧原始探针私有阶段日志：connection-check从elapsed 2660ms到再次acquiring 3231ms，约 **571ms**，之后严格帧序报错；启动枚举约563ms。可确认连接检查阻塞取流，但不能把571ms全归给单一SDK调用，或认定所有跳帧由此造成。该阻塞降低采样频率、可能让OneByOne有限缓冲积压，且不计入下一帧的旧ObservedAt时延。

不能直接删除身份复核、调低频率或开并发SDK线程消除该开销。已读本机官方头文件：`MV_CC_GetDeviceInfo` 明确提示GigE取流中有阻塞风险，不能未经测量替换后宣称解决。本包保持原身份检查，仅拆分计时。

## 帧序、丢失与候选

- 旧decoder=null探针要求帧号差为1；软件路径仅要求严格递增。14帧后失败证明严格门禁被触发，不证明硬件损坏，也不是有效的15秒吞吐基准。
- `skippedVideoFrames` 是帧号差减1的累加：旧1624是跳过帧数，**不是1624次间隔事件**。新增videoGapEvents单独计次数。未解码原始帧与业务输入丢失是不同事实。
- 有限缓冲、同步解码、健康检查可产生采样间隔，网络缺失完整帧也可能跳号；仅跳号不能区分。返回帧LostPackets非0、无效长度/布局、重复/倒退仍故障并归还借帧，不将允许采样扩到坏帧。
- 识别后交付false/抛异常保留完整Undelivered并拒绝重启；持久Pending/Interrupted属Application责任。旧122笔输入均Processed，对应119笔作业、2笔多码和1笔超时，数量闭合，没有已识别结果静默丢失证据。
- 三轮共3帧有两个同内容候选。相同文本不证明同一物理码，继续多码拒绝；不选首条、不合并文本、不减小候选上限或码制集合。

## 定向测量及修复

下述为Windows候选脏树测量，信息版本仍带基线提交；不能记为f870ffc0原生产性能。私有原图、码、身份、账本及完整日志只留D盘Git外；公开仅数字摘要。后续干净提交回归另记精确来源及程序集SHA。

重放既有30张3072×2048私有Mono8，各3次共90次，含冷启动。这批图像不是旧120秒的完整逐帧原件。前后均ZXingCpp 0.5.3/内核3.1.1、同一完整搜索参数。每张重复候选内容/重数相同，前后逐图候选数量相同；72次空结果、18次单候选，不据此验收多码实物场景。

| 阶段 | 原处理累计/P50/最大 ms | 等价优化后累计/P50/最大 ms |
|---|---|---|
| 对比度 | 2650.063 / 17.700 / 58.162 | 202.157 / 0.638 / 7.159 |
| ZXing调用 | 26531.327 / 215.066 / 870.299 | 26145.322 / 199.822 / 724.408 |
| 完整Decode | 29186.206 / 229.452 / 924.807 | 26352.907 / 200.361 / 728.035 |

原处理约91%的Decode墙钟在ZXing调用内，约9%在对比度。SIMD范围扫描＋至多256项查表替代逐像素除法，使优化阶段累计减少约92%。全部32,896个lo/hi组合逐像素对照旧公式，包含常量/全范围、向量边界/尾部和池数组外区域；整数取整及原生输入保持一致，不用减少识码搜索换性能。

ZXing墙钟包括计算和被调度挂起，不能全称CPU计算。优化后90次样本进程CPU约11.09秒，Decode墙钟26.35秒；无调用栈/等待证据，无法把差额直接归因于某锁、GC或系统。新入口记录每次CPU/Gen2增量；前后并非独占CPU基准，不外推整条生产吞吐改善比例。旧1569ms单笔缺阶段原件，未被本次离线复测精确归因。

另用三张6MP合成有码帧（附小尺寸空帧）贯穿实际ZXing、合成MVS端口、Application/SQLite和HTTP适配器。三个同码输入保持三份原请求、一次发送、全部SlotResolved。有码帧复制最高8.962ms、清零最高5.459ms、解码133.727～152.684ms、同步持久交付3.154～28.900ms、观察到交付完成139.578～198.696ms。这些定位该合成运行的开销，**不是SDK复制实测、真实场景上限或客户RCS验收**。

计时保留会话累计数量/总和/极值，各阶段分位数仅取最近512样本，逐帧关联也最多512条，新epoch清零。空帧亦有轨迹，宿主交付失败保留Delivered=false；解码异常/停止中识别仍以原Failure/Undelivered为准。SDK时间戳来自核对后的12/16/24偏移，仅保留原值；**未证明频率/时基映射，不能直接换算曝光UTC或帧龄**。

### 干净提交、同一二进制对照

代码提交 `14eead38b828af1e1b32b9be671f38015dbf78e6` 的干净树重建后，在同一个验证二进制内注入测试专用原标量公式，再顺序运行默认生产SIMD/查表公式。每组均为同30张私有图像×3次；两个运行回执WorkingTreeDirty=false、程序集SHA相同，逐图候选数量一致。参考公式仅验证入口可选，正式装配不提供配置开关。SDK、码制、候选及责任守卫保持原实现。

| 阶段 | 原公式累计/P50/最大 ms | 生产公式累计/P50/最大 ms |
|---|---|---|
| 对比度 | 1881.166 / 7.989 / 45.724 | 231.261 / 0.800 / 12.351 |
| ZXing调用 | 25354.300 / 205.724 / 711.433 | 27766.064 / 184.482 / 1413.443 |
| 完整Decode | 27240.286 / 215.754 / 751.975 | 28002.715 / 186.517 / 1417.484 |

局部对比度累计减少约87.7%，**总体Decode累计墙钟本轮反而增加约2.8%**。不能把局部优化写成完整链性能通过。生产公式轮最长的是一张空结果帧：Decode墙钟1417.486ms、进程CPU406.25ms、该次Gen2增量0；原公式对同一图像最长751.977ms/CPU250ms。可确认显著非CPU墙钟成分、未发生该次Gen2 GC；不能因此断言具体调度器、原生等待或共享锁根因。图像内容影响与运行负载尚需ETW/线程栈区分。这也不等于重现了旧120秒里有码输入的1569ms故障。

## 真机补采

新 `--mvs-throughput` 已实际尝试，SDK导出/版本加载成功，但未发现匹配原IP/序列号的设备。0帧，未Create/Open/Start，Initialize后完成Finalize，退出1。不能据此断言设备总数为0或硬件损坏。本轮没有新增真机吞吐、拔插或完整时延通过证据。

接通原相机、关闭占用程序后，在相同SDK/物理身份/Mono8/曝光/ROI/缓冲/网络下顺序测量，避免并发构建争用CPU。先读回实际帧率、曝光、ROI、PayloadSize、节点数和策略，不修改为更易通过的值。

1. **原始吞吐**：新采样探针60秒至少3轮，旧严格入口另作对照。按首末有效返回时间计算delivered fps，记录有帧/NoData、跳过帧数/事件次数、逐帧序号/时间戳和health分段。退出3仅表示采图专用/无识码能力，还须核对Failure为空、有效帧数和完整时长，不是识码通过。
2. **丢失归因**：用已安装官方SDK `MV_CC_GetAllMatchInfo(MV_MATCH_TYPE_NET_DETECT)` 读开始/结束接收帧、丢失帧/包、请求重发/已重发包差量；SDK缓冲丢弃另查公开网络传输统计。按对应版本头文件/工具读数，缺项写Unavailable，不把帧号差当网络丢包。新增代码前须核验导出/x64结构/有效阶段/停止责任；本包未新增网络统计ABI。
3. **真实软件链**：新二进制至少120秒，保留mvs-timings.json；有码/空帧/多码、冷/热帧和>1500ms原件分组。按Number、持久SourceEventId=mvs.frame.N和原epoch对账，SDK返回→receiver、提交、queue_ms、processing_ms、RCS尝试分开。排空后核对全部Pending/Processed/Interrupted、拒绝和原请求/响应/Unknown，不能只读终止瞬时快照。
4. **帧龄/调度**：先验证设备tick频率及SDK host timestamp单位/时基再建立映射，分离曝光/传输/排队；未验证只比较原值增量。最长帧同时采WPR/ETW CPU调度/等待、GC和磁盘提交证据，区别原生计算、调度与入账阻塞。
5. **#152生命周期**：同一受保护正式根/随包默认工厂，验证启动离线→接通、运行拔线→插回、调用中停止、重试退出和双相机隔离；核对新epoch/序号、旧回调拒绝、未交付识别围栏、真实Destroy/Finalize及原请求不重发，不借新目录清除责任。

## 复现

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-contrast-checks
python scripts/measure-mvs-latency.py --data-root D:/ImporterPc.LocalValidation/NEW-OFFLINE-MEASUREMENT
# 私有图像另加 --private-pgm-directory <绝对PGM目录>，不输出原图/码内容。
# 同一二进制顺序A/B：另一全新输出根加 --reference-contrast，复现原公式；默认即生产公式。
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-software-flow --six-megapixel
python scripts/verify-sm-sdk-python.py --data-root D:/ImporterPc.LocalValidation/NEW-SYNTHETIC-PYTHON
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:IpcContractPath=<绝对本轮Ipc.Platform.dll路径> -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
```

原始实采命令：`Probe.dll --mvs-throughput <核验SDK目录> <原IP> 60 --serial-file <绝对私有序列号文件> --timeout-seconds 100`。保留 `--mvs-connect` 的严格连续帧语义。两者仅采图，不写触发/曝光/像素参数，不发RCS。真实软件链沿已有 `--mvs-software-flow --live-ip ... --sdk-directory ... --rcs-endpoint ... --seconds 120`（或明确bundled路径）；仍限获授权本机模拟RCS和隔离身份，不等于正式随包资格。

## 验证状态

数字/精确来源见[摘要](evidence/mvs-latency-01/summary.json)。本机完成Application/Probe/SM Desktop Release构建、灰度等价、MVS专项、完整Probe自测、6MP合成完整链、Python账本17项、离线启用/自动重连、内置装配、RCS超时/Unknown重启/心跳回归。计数为PASS断言行，不是现场用例数。

- RV-MVS-LATENCY-01：PASS_SOURCE_ANALYSIS，版本/时钟/责任边界已区分。
- RV-MVS-LATENCY-02：PASS_LOCAL_SYNTHETIC，采样仍拒绝重复/倒退/坏帧；旧严格合同、延迟注入及有界资源通过。
- RV-MVS-LATENCY-03：PASS_OFFLINE，实际ZXing私有图像重放及像素完全等价，不是生产时延验收。
- RV-MVS-LATENCY-04：PASS_LOCAL_SYNTHETIC，完整持久链及原请求/Unknown回归，不是客户RCS验收。
- RV-MVS-LATENCY-05：NOT_VERIFIED_DEVICE_UNAVAILABLE，未发现匹配设备；新吞吐、#152拔插、真机完整时延、目标机及长稳待补。

不关闭旧RV，不改1500ms或ObservedAt，不放宽完整候选、多码、SDK完整性/ABI/身份、原请求和Unknown责任。CI停用，未安装/发布新介质。
