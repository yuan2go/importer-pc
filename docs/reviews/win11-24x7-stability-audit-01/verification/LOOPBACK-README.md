# 剩余验证：双 TCP 相机与 RCS 合成负载

脚本 `loopback_load.py` 使用 Python 3.10+ 标准库，仅监听 `127.0.0.1`。它不修改应用配置、Windows网络、设备或生产数据。仅用于隔离测试机/测试账户与独立数据根；生产安装不应指向此夹具。真实SDK与相机测试须另做，不能被此脚本替代。

## 配置匹配

通过原应用支持的隔离 Simulation 配置/受控激活流程，选择已受验Release及其真实提供者版本：`tcp-line 1.0.1` + `sportmaster-rcs 1.3.1`（提交c91559f6基线）；保留所有原装配/权限守卫，禁止伪造生产资格。脚本不会代建签名能力或激活配置。

|字段|隔离值|
|---|---|
|左Reader IP/port|127.0.0.1 / 19001|
|右Reader IP/port|127.0.0.1 / 19002|
|entity.mode|line，仅本合成夹具“一行一实体”|
|Reader bcr-code|AUDIT-L / AUDIT-R，各不相同|
|StationCode|AUDIT-STATION|
|WCS Target|http://127.0.0.1:19080/rcs/sorter/api|
|RCS source / locale|AUDIT / zh_CN|
|zone-id / project-id / quantity|1 / 1 / 1|
|specified-chute|false；不填写chute-code|
|request-id-base / request-id-count|1000000000000 / 1000000000；仅独立合成库，不是现场分配|
|left-station-code / left-bcr-code|AUDIT-LEFT / AUDIT-L|
|right-station-code / right-bcr-code|AUDIT-RIGHT / AUDIT-R|
|heartbeat-interval-seconds|5|
|条码筛选|能接纳 AUDIT-L-0000000001 / AUDIT-R-0000000001 形式，不能把例值当生产规则|

固定运行组合的完整配置/版本/hash。MaximumOperations 约束不可归档责任，并非累计历史总件数；不能将其误解成处理10000件必停。任何队列、超时、编号范围或容量参数调整都应另立实验并明确差异，不能清理业务账或绕过未决守卫来宣称原配置通过。

## 正常24小时：独立节拍

PowerShell在隔离机执行（例中双位各1/s只是未取得现场节拍时的建议演示值，先替换为实际额定/峰值）：

```powershell
python .\loopback_load.py --output D:\IpcAudit\load-normal-001 --duration 86520 --warmup 120 --left-hz 1 --right-hz 1
```

`--duration` 是从发生器启动起算的总调度时长，**包含 `--warmup`**；实际计划发码窗口为 `[warmup, duration)`。上例120秒预热后，才有完整 `86520 - 120 = 86400` 秒的双位正常负载窗口。原先 `--duration 86400 --warmup 10` 只计划86390秒负载，不能据此宣称完整24小时正常负载通过。

先启动监听，在120秒预热段内启动/恢复同版隔离应用，并用应用证据确认两侧Reader已Ready、共享RCS与存储及业务准入守卫均已就绪。**只统计两侧已Ready后的连续完整86400秒正常负载**；应记录应用Ready时刻、首个计划输入时刻及正常负载起止，不能只使用脚本进程存活时长。脚本不查询应用Ready，也不自动等待它。如果任一侧晚于第120秒才就绪，或出现缺连接、非预期停滞或干预，本例就不足以取得该完整窗口；保留本次失败/不足记录，另用足够预热的新实验重新计证，不把未就绪段补算进去。

预热、负载结束后的排空/关闭、故障恢复均单独记录，不计入86400秒正常负载。脚本目前没有独立排空参数，到点会关闭监听和现有连接，**这不是有序排空已经完成的证据**；必须另外核对最后输入与全部在途请求的完成/未决去向，记录到点关闭带来的事件，不将其误算成正常完成或悄悄丢弃尾部输入。

输出目录必须不存在，脚本拒绝覆盖原证据。`--left-hz 0 --right-hz 0`可生成完整空闲窗口；一侧为0可测单侧负载。空闲及单侧负载另行报告，不能代替双位额定正常负载。峰值/突发用单独明确时段实验及不同目录；脚本始终按独立时钟发码，不等待RCS回应或应用完成上一件。

每次计划输入都有 sequence、barcode、计划时刻与调度滞后；`input_written_to_socket_buffer`仅表示交给本机socket缓冲，不表示应用接纳。缺连接、发生器落后或缓冲超限另记失败。RCS收到的原requestId/请求hash、发送回应及回应hash独立记录；发送回应也不表示应用取得。

最长7天、每位最高100Hz、32个HTTP连接、输入正文64KiB、socket缓冲约64KiB，证据默认上限1GiB；达证据限额明确中止且标为证据不完整，不能当正常跑满。按负载与磁盘预算显式调整 `--max-evidence-bytes`。脚本自测3秒不能证明应用长稳。

## 故障恢复实验：与正常长稳分开目录

```powershell
# 慢RCS：5000ms只是例值，应大于实际Effective.TimeoutMs来验证Unknown责任
python .\loopback_load.py --output D:\IpcAudit\slow-rcs-001 --duration 120 --warmup 10 --left-hz 1 --right-hz 1 --rcs-delay-ms 5000

# 第3个取格口请求在夹具收到后断连接，不伪造“没发出去”
python .\loopback_load.py --output D:\IpcAudit\lost-reply-001 --duration 120 --warmup 10 --left-hz 1 --right-hz 1 --drop-every 3

# 相机第10个计划输入时主动断TCP；应用应按原策略显式维护，不应自动重发
python .\loopback_load.py --output D:\IpcAudit\camera-close-001 --duration 120 --warmup 10 --left-hz 1 --right-hz 1 --disconnect-every 10
```

另有 `--fail-every N`（每N个取格口HTTP503）、`--mismatch-every N`（回应requestId+1）、`--invalid-every N`（非法UTF8）、`--oversize-every N`（4097字节行）。每次只启用针对风险的条件，保留所有人工维护时刻。相机主动Close是EOF实验，**不等于静默黑洞/物理拔线**。本夹具心跳保持正常响应；共享RCS完全中断需要独立受控网络/进程停止实验并记录干预。

## 判定

必须另采应用 InputObserved、接纳/拒绝/完成/未决、队列/等待时间、WPF操作延迟和资源曲线，把 `(position,barcode)` 与原业务请求/回应对账；夹具不读或改应用DB。确认左右格口分别为L07/R09且对应自身当前barcode/requestId，故障后不可借旧格口显示新任务成功。`summary.json.soak_pass`始终为NOT_DETERMINED，不能仅看脚本运行到点宣称通过。若脚本/应用提前停、重启、人工干预、证据限额或调度丢时段，都记录为独立异常。

真实海康路径仍需目标Windows11版本/构建号、SDK2.0.0.4及完整同位数依赖hash、设备型号/固件/网卡/电源条件，并用实际双机完整24小时正常负载、另做断电/拔网/重连/SDK停机寿命测试。此脚本没有这些覆盖。
