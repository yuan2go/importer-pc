# 双相机读码性能测量

工作包：[SM-DUAL-CAMERA-PERFORMANCE-33](../work-packages/WP-IPC-SM-DUAL-CAMERA-PERFORMANCE-33.md)。基线 main `38cb7dc0`，2026-11-03。当前为内置 MVS 采图、进程内软件解码、内置 RCS；沿用 [28 识别规则](sm-reader-robustness-28.md) 和 [29 一次放置规则](../work-packages/WP-IPC-SM-SAME-PLACEMENT-29.md)。本包不改变读码搜索、相机参数、放置判定、请求、权限或生产配置。

## 执行前

目标机需要 Python 3.10 或以上（仅标准库，不需要 pip 包），以及包含本包诊断字段的受控软件版本。脚本不会安装 Python 或软件。PR182/旧版本可采计数，但没有新逐帧/请求字段时会报告缺项，不能以 0 ms 代替。是否部署新版本由现场另行决定；本次未部署。

未来 SM 发布介质包含 `tools/dual-camera-performance.py` 和 `SM-DUAL-CAMERA-PERFORMANCE.md`；源码入口为 `scripts/dual-camera-performance.py`。单独分发此脚本及说明也可以。所有命令在普通 PowerShell 中运行，读权限不足就停止并按实际权限处理，不修改 ACL、不提权绕过生产守卫。

先在**实际数据根**找到该运行实例的文件。默认新站通常为：

```powershell
$progress = 'D:\ProgramData\ImporterPc\data\Production\station-01\sportmaster\runtime-progress.json'
$snapshot = Get-Content -LiteralPath $progress -Raw | ConvertFrom-Json
$snapshot.Progress | Select-Object At, ProcessId, RuntimeId, SourceVersion, SchemaVersion
$snapshot.Progress.Connections | Select-Object DeviceId, Epoch, Health
```

这里的 `DeviceId` 是采集实例 ID，不是物理序列号、IP 或 RCS 的 bcrCode。按实际生效配置确认左右映射，禁止只按枚举顺序猜左右。默认路径只是示例；文件不更新或不存在，先调查运行实例与权限。不要自动启动/重启业务或相机。检查磁盘余量，预留采集日志和分析库空间，数据全部留 D 盘、Git 外。

## 三组可复现实验

先记录实际版本、CPU/逻辑核数、内存、电源模式、相机分辨率/取帧设置、曝光、照明、码制多选、反色、软件筛选规则及 RCS 类型（本机模拟/客户接口）。曝光、焦距、速度是人工现场记录，本脚本不读写设备。实际软件码制与反色保留在快照 `Performance.DecoderConfiguration`；帧尺寸在逐帧记录中。驱动/SDK 时间戳尚未标定，不能把它换算为曝光到主机的延迟。

使用同一软件版本、同一参数、同批包裹和摆放/反光条件，分别安排左侧供包、右侧供包、双侧同时供包。`--scenario` 只标记实验，不启停相机，不改变任何一侧配置。若另一台相机仍采空托盘，也会占用采集/解码资源；如实记录该事实。需要“仅一个采集实例”对照时，由现场按已有授权手动安排独立实验，不能把左侧供包自动称为只运行一台相机。

以下 30 秒预热、300 秒测量、1 秒采样是可修改的实验安排，**不是性能合格线**。重复各组、轮换顺序并保持同等环境；每轮使用新目录。先人工按已授权操作使应用正常运行，再启动被动采集：

```powershell
$tool = 'D:\IPC-Perf-Tools\dual-camera-performance.py'
$left = '实际左采集DeviceId'
$right = '实际右采集DeviceId'
python $tool collect --progress $progress --left-device $left --right-device $right --scenario left --warmup 30 --duration 300 --interval 1 --max-mib 2048 --output D:\IPC-Perf\left-01
python $tool analyze --capture D:\IPC-Perf\left-01 --output D:\IPC-Perf\left-01-analysis

python $tool collect --progress $progress --left-device $left --right-device $right --scenario right --warmup 30 --duration 300 --interval 1 --max-mib 2048 --output D:\IPC-Perf\right-01
python $tool analyze --capture D:\IPC-Perf\right-01 --output D:\IPC-Perf\right-01-analysis

python $tool collect --progress $progress --left-device $left --right-device $right --scenario dual --warmup 30 --duration 300 --interval 1 --max-mib 2048 --output D:\IPC-Perf\dual-01
python $tool analyze --capture D:\IPC-Perf\dual-01 --output D:\IPC-Perf\dual-01-analysis
```

采样不能快于软件自身约 1 秒的文件发布获得更高原始时间分辨率。快照内 512 条窗口用于补齐两次采样之间的逐帧记录，重复部分按序号去重。额度达到 `--max-mib`、Ctrl+C、读失败均保留已有证据并标出不完整；不会删旧日志腾空间。额度上限最多超出一条采样记录。外部日志写出/分析失败返回非零，保留原目录，使用新目录重跑。不要将这些命令指向应用运行数据目录作为输出。

测量期间记录人工放置台账（Git 外）：每次放置的本地编号、左右位、放上/取下 UTC、预期完整码值、是否有反光/斜放/真实运动、是否读对、是否得到正确格口。相同码的两次实体放置仍要分两行。另记录“有效可读标签在视野中的最短停留时间”和双位实际放置间隔。与原业务记录人工核对，不按条码文本相同推定同实体。没有这些独立真值，脚本只报告帧级与请求级事实，**不能计算包裹正确识别率、误选率或串格口率**。

模拟 RCS 与客户 RCS 的轮次分别记录，不能混在一组性能数字里。脚本不发测试请求、不启动模拟器、不重放 Unknown。不要为测量临时开启详细日志、调试读码或人工补录；这些都会改变工作负载，若确实发生须单列。本包没有取得客户接口调用或现场过机授权。

## 输出和统计口径

| 文件 | 内容 |
|---|---|
| `capture.json` | 场景、设备映射、起止、Python/平台、脚本哈希、采样计划、原始流哈希、是否完整 |
| `samples.jsonl` | 只读快照及采集时间、读取开销、预热/测量标记；读取错误单列，不复用旧快照冒充更新 |
| `summary.json` | 左右增量计数、资源峰值、同采样间隔两侧均取帧的区间数、异常、精确分位数；固定保留未验收结论 |
| `quantiles.csv` | 左、右、`combined` 样本池，以及整个 `process` 的各指标计数、最小、均值、最大、P50/P95/P99 |
| `correlations.csv` | 每次已收集的 RCS 提交结果对应的设备/epoch/帧/观察/作业/原请求和呈现关联；缺失不补旧结果 |
| `stages.json` | SDK/主机/解码内部阶段的计数增量、均值，以及最后 512 样本的窗口分位数 |
| `analysis.sqlite` | 脱离业务数据库的离线统计中间库，保证长采样无需把所有帧保存在内存 |

Pq 用精确 nearest-rank：排序后第 `ceil(q × N)` 个样本；无样本不输出该行，不填 0。`combined` 是左右实际样本合池，**不是两个 P95 的平均值**；三组实验分别比较同一指标、样本数和异常覆盖。CPU 是应用进程累计 CPU 对单调时间的增量，`CpuOneCorePercent` 可大于 100%，`CpuCapacityPercent` 再除应用可用逻辑处理器数（Environment.ProcessorCount，可能受进程配额/亲和性限制；不是全机负载）；不能拆成两台相机的 CPU。采集器自身是另一个进程，其负荷不包含在应用 CPU 中。建议同等条件加做采集前后对照，并检查读取开销、日志体积和磁盘负载。

| 阶段 | 量与边界 |
|---|---|
| 取帧 | `Frames` 为通过校验的 SDK 帧数；`AcquireCalls` 不是有效帧；`NoData` 不是识码失败；`SdkFrameNumberGaps` 是 SDK 帧号缺口，不能等同未解码帧替换 |
| 采集成本 | `sdk.acquire.frame`、`sdk.acquire.no-data`、`sdk.release`、`host.delivery-interval`、`host.capture-work` 等现有阶段；计数/总时长可作测量区间差，窗口 P95/P99 仅代表最后 512 次，不能冒充全程分位数 |
| 待解码 | `DecodeQueueMs` 从主机取得帧的时间到进入解码处理，包含该帧复制/SDK 归还及调度时间，不是无限 FIFO 长度；原图容量等待另看 `image.original-capacity-wait` 和状态采样 |
| 帧替换 | `SupersededDecodeFrames` 包括最新待解码帧替换、停止/边界丢弃、容量等待后选择更新帧，不能单独归因为 CPU；只表示尚未形成识别责任的帧被退还 |
| 解码 | `DecodeMs` 分位数只统计成功完成解码的逐帧样本（含成功解码但无码的帧）；跳过、暂停和拒收不伪装成 0 ms；失败成本可看所有尝试的内部 `decode` 阶段窗口与均值 |
| 有效识别 | `DecodeCompleted - EmptyFrames` 为有候选帧数，`Candidates` 是候选总数；还需经过筛选、完整性及准入。`Delivered` 是交接，不等于新放置或 RCS 接受 |
| Application | `HandoffMs` 是驱动到持久交接耗时；`IngressQueueMs`、`IngressProcessedMs` 是消费等待和处理（可能包含 RCS/持久化）的分布；worker 队列深度单列 |
| 放置/请求 | `InputAdmitted`、`SamePlacement`、`InputRejected/Blocked/Duplicate` 分开；只有新放置才发起一次请求。`RcsSubmitStarted/Completed` 与 `RcsQueryStarted/Completed` 分开，查询不计作新提交 |
| RCS/格口 | `RcsSubmitCompletedMs` 包含调用边界及结果提交，不是纯网络 RTT；`DecodeToRcsResultMs` 用同一原始观察的单调时钟。`SlotComposedMs` 是解码至现有 DWM 呈现报告；结果至格口差值只在同一观察与工位匹配时可计算 |

逐帧标识使用 RuntimeId + DeviceId + Epoch + TraceSequence；帧号本身不能跨左右或重连去重。作业 `ObservationId` 是规范化身份，本包在既有呈现队列中按设备/epoch/SourceEventId 映射回原始观察；窗口已退役时保留 null，不猜匹配。取帧与结果/呈现是异步边界，采样窗口两端存在未完成工作：计数比例是**完成区间**统计，可能有前轮在途进入或本轮在途尚未结束，不是完整放置队列守恒式。`correlations.csv` 标出已启动但未采到结果的情况；无码、空托盘、多码、重复放置均不应强行一帧对应一次请求。

呈现指标是已有的同进程解码完成至 DWM 合成栅栏，不代表相机曝光到屏幕光子，也不能证明现场格口物理正确。非当前观察可能已被更新画面替换，没有 `SlotComposed` 并不自动等于 RCS 丢回包。

同一观察有完整关联时，另输出 `HostFrameToRcsResultMs` 和 `HostFrameToSlotComposedMs`：用驱动单调时钟的 `HostDeliveryToDecodeMs` 加同一观察的解码后耗时。边界从 SDK 已将帧交给主机起算，不含曝光和未标定的相机传输年龄；不把 SDK 原始时间戳当主机时间，不把 `ObservedToReceiverMs` 与解码后耗时相加造成重叠计时。

## 异常和判定

| 异常分类 | 调查方法 |
|---|---|
| `COLLECTOR_READ_ERROR`、`STALE_PROGRESS`、`LEGACY_TELEMETRY`、`*_UNAVAILABLE`、`INSUFFICIENT_DISTINCT_SNAPSHOTS` | 核对权限/实例/版本/是否退出；先补数据，不得宣布性能通过 |
| `*_WINDOW_LOST_*` | 窗口已覆盖未采到的样本，数值表示缺条数；这些分位数只代表保留样本，不能叫全程分位数。缩短采样间隔、降低外部负载后另轮重采 |
| `RUNTIME_CHANGED`、`CAMERA_EPOCH_CHANGED` | 分段相减，绝不跨重启延用累计数；核对重连、退出和原责任。原请求不重发 |
| `COUNTER_RESET_WITHIN_EPOCH`、`TRACE_COUNTER_RESET`、`FLOW_COUNTER_RESET`、`NON_MONOTONIC_RUNTIME`、`UTC_CLOCK_CHANGE`、`INVALID_NUMERIC_METRIC` | 调查版本、时钟和采样顺序。UTC 跳变不用于改写单调解码耗时；旧版依赖 UTC 的速率明确降级 |
| `DECODE_REJECTED_FRAME`、`CAMERA_FAULT`、`DECODE_PAUSED`、`ORIGINAL_CAPACITY_WAIT`、worker/runtime failure | 对照坏帧、资源所有权、调试或业务守卫；不能把所有等待归因算法 |
| `RCS_UNKNOWN/REJECTED/CONFLICT`、`RCS_DEADLINE_EXPIRED`、`RCS_STARTED_WITHOUT_COLLECTED_RESULT` | 分开调用超时/业务拒绝/身份冲突/测量截止仍在途；Unknown 核对原请求，不自动重试 |
| `FLOW_DEVICE_POSITION_MISMATCH`、`SLOT_POSITION_MISMATCH`、`RCS_FRAME_NOT_CORRELATED`、`SLOT_BEFORE_RESULT_OR_CLOCK_MISMATCH` | 视为关联证据异常，核对左右映射与来源；不展示一条“成功全链路”掩盖缺失 |
| `NON_CAMERA_FLOW_EVENT`、`UNMAPPED_CONNECTION`、无某侧采集进展 | 核对人工输入、实验场景和设备映射；双相机并发条件不足时，不宣称双机性能已测 |

异常计数有的是受影响快照次数（例如 Faulted/等待），有的是缺失事件数或实际结果数；不要将所有异常计数相加当作失败包裹数。`BothCamerasCapturedWithinInterval` 仅证明同一采样间隔两侧均有取帧进展，不证明两个解码 CPU 区间严格重叠。

先检查证据覆盖，再比较三组的 Decode/Queue/Handoff/RCS/Slot 分位数、替换计数和资源曲线。用 `samples.jsonl` 按时间查看替换突增是否伴随解码耗时上升、CPU 增长、原图容量等待或 RCS/持久队列增长。若 RCS 变慢而解码不变，优先调查请求/交接；若 SDK 帧号缺口增长，单列采集/网络；若两侧解码同时变慢并伴随 CPU 增长，才形成 CPU 竞争的待验证假设。

是否满足节拍，必须结合现场给定的有效标签停留窗口、并发包裹间隔、空托盘释放流程、允许的响应时间及独立真值。没有明确要求时输出 `NOT_EVALUATED_NO_SITE_SLA`，不给出“合格/不合格”阈值。高帧替换比例既不能证明漏包，也不能证明不会漏包。真实运动模糊、实物成功率、客户 RCS 和物理完成仍需现场验证。

## 开发复现与验证记录

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification/Ipc.Application.Verification.csproj -c Release
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll D:/w/ipc-dual-camera-perf-32 --dual-camera-performance
python -m unittest discover -s scripts -p test_dual_camera_performance.py -v
```

验证结果在本包提交前补记精确来源。原生采集探针只运行 `--mvs-self-test`/`--mvs-pipeline` 合成端口，不能运行 `--mvs-connect`/`--mvs-throughput` 代替授权。真实设备、目标工控机双相机连续过机、真实运动模糊、客户 RCS、现场长期成功率及验收：**NOT_RUN**。
