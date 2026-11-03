> 2026-11-01 用户后续纠偏：新观察立即清退旧格口，废止本页1500ms同码保留显示；主动推送、分阶段反馈、按观察身份合成与预览缓冲优化见 [SM-RESPONSIVE-PRESENTATION-16](sm-responsive-presentation-16.md)。本页代码/验证保留形成时SourceCommit，不倒写历史结果。

# SM感知时延与显示稳定性15

2026-11-01。范围及本轮决定见[工作包](../work-packages/WP-IPC-SM-PERCEPTION-LATENCY-15.md)。继承 PR167 / `5ae7bd59`，修改前当前指南见[原字节归档](../archive/pre-sm-perception-latency-15/manifest.json)。旧真机数据、原30张图、SDK、配置、账本和历史D/RV不改写。

## 四项实现

1. Presentation 为同位、同码、同连接 epoch 保存最近已确认格口，待新请求时最多1500ms显示为“上次格口 · 确认中，不能据此投递”。原 `Slot`、`CurrentTaskAvailable`、作业、请求和命令守卫不改变；波次/任务/SKU/数量全部退出。期限不能因刷新续期；拒绝、Unknown、不同码、来源/配置变化和失效不会借用旧值。快速反馈已先到而一致性快照仍是上一条成功时，也只作为上次结果显示，不冒充新请求接受。
2. 同一 MVS 所有者下分离专用采集和解码线程。SDK完整帧→托管复制→归还该借帧→单个最新待解码槽→原图副本/对比度/完整ZXing→独立观察/持久交付。最多1个处理、1个待解码和1个临时复制数组，不暴露原生指针；原图32帧/256MiB预算保持。替换仅发生在未解码视频，独立计数 `SupersededDecodeFrames`，不丢弃已识别输入；后者交付拒绝仍保留原观察并阻断重启。隔离切换清空未识别待处理帧并等待真实解码退出，再清SDK缓存；取消等待不伪称释放。
3. MVS4.8.2.1 本机头文件核对 `LatestImagesOnly=1`，软件识别配置此策略；严格采图/原始吞吐仍为 `OneByOne=0`。启动/重连核验IP、序列号与独占句柄；持续完整有效帧来自该句柄，可证明存活，稳定取帧时不做每秒全局枚举。无新帧时仍执行连接/身份/触发读回，SDK操作保持单一所有者串行。未取消坏帧、像素、ABI、身份或未交付输入守卫。SDK帧号间隔与宿主槽替换分别记录，不能视为网络丢包，更不能证明物理包裹不漏扫。
4. 同进程100ms轻量只读反馈显示“已读到”，短暂边框与最多每秒一次系统短音不表示准入/RCS接受。合成测量使用 WPF Rendering→Dispatcher空闲提交→后台 `DwmFlush`→原观察身份回报；进程全局最多一个实际在途 fence，忙碌或失败不记成功。记录解码完成→反馈合成、解码完成→格口合成、快照发布→格口合成，最多512条，进入 `runtime-progress.json` 的 `Presentation`。它包含该次实际RCS等待，不用127.0.0.1数据替代客户往返，也不把它称为曝光/物理屏幕光子时延。诊断 fence 不加入业务/窗口关闭排空；只有实际原生调用返回才释放全局槽，避免合成器阻塞拖住设备关闭或产生无限后台调用。

`DwmFlush` 的边界依据 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush)：等待本进程已排队的图形更新到下一次呈现；并非整个会话刷新。窗口隐藏、卸载、最小化、过期或身份变化时不记成功；合成失败单独保留为不可测。声音走系统声音配置，未声称实际现场扬声器验收。

## 计时与召回口径

SDK返回时的 `ObservedAt` 不挪动。新增 `host.decode-queue-wait`、逐帧 `DecodeQueueMs` 和未解码替换标记；复制、归还、解码、持久交付和最终清零仍分段记录。`ExposureFrameAge` 明确为不可测，直到目标设备时间戳频率、SDK主机时间戳单位和时基映射验证完成；不得将最后一次SDK返回距今的时间当曝光帧龄。

测试专用参数比较入口 `--mvs-latency --compare-fast-search` 同批同图比较完整搜索和 `TryHarder=false`，同时记录耗时和完整候选多重集合是否一致。生产保留全码制、旋转、反色、下采样和完整搜索，不因某批图更快便削减候选范围。确定无对比度的纯常量Mono8帧直接为空，不进入原生搜索；与真实完整ZXing逐一核对全部256灰度值，其他图像仍完整搜索。

## 已执行与待验

最终产品源码为 `05a4b9abb049f876ad23b895350878d2e3a1de32`，在干净源码提交后重新构建并执行全部本轮定向套件；后续提交只记录证据/文档。精确源码指纹、复现命令、原始正负日志和统计见 [summary.json](evidence/sm-perception-latency-15/summary.json)。构建、合成端口、原生Windows、离线实图、真实SDK及安装分别记录。

| 验证层 | 最终结果 | 证据边界 |
| --- | --- | --- |
| Probe、Application verification、WPF verification、Sportmaster Desktop Release | 4个构建，均0警告、0错误 | 验证工程全量Rebuild；不等于安装验收 |
| MVS生命周期/压力/预览调度 | 99条断言PASS | 合成SDK端口；含87条既有防护、8条管线和4条受限线程池调度 |
| Application原图、扫码轮次、停止、手输、RCS错误、离线恢复及像素等价 | 211条断言PASS | 原生软件解码、合成相机/RCS；不是真实客户往返 |
| 原生Windows WPF显示、持续输入、视频生命周期/轮次 | 126条断言PASS | 实际窗口、定时器和DWM；包含13条本轮显示与合成测试 |
| 历史30图×3次完整搜索 | 90次识别、60条重复候选一致性断言PASS | 真实离线像素；不代表实时相机/物理包裹召回资格 |

共436条最终定向断言；离线重复一致性60条另计，不把断言数当现场案例数。压力夹具阻塞首张解码，采集/预览仍各完成20帧，宿主未解码替换18帧；仅帧1及20识别，两个同码结果仍有独立原观察。启动身份枚举保留；持续有效帧不再周期枚举；无帧时测得合成80ms枚举并明确故障恢复。此80ms是注入值，不是现场SDK测量。

未修改产品的 `5ae7bd59` 负向对照实际失败：阻塞解码时不能采到第二帧；同码下一请求待定时旧格口被清除。独立夹具、入口补丁及失败原日志均保留。中间出现的测量夹具竞态、旧视频突发假设、800 DIP紧凑布局回归及停止异常分类回归也保留FAIL日志及修正说明；最终重跑未删原守卫/布局断言。

## 历史原图参数实验与决定

从历史采图工程/受保护配置找到同一批30张3072×2048 Mono8 PGM，原始文件散列核对未变。实际ZXing-C++逐图比较完整候选字符串多重集合，包含重复项；只公布数值/匹配标记，原图、真实条码、IP/序列号与私有配置不入库。数值见 [离线汇总](evidence/sm-perception-latency-15/offline-replay-summary.json) 与 [逐次计时/候选一致性](evidence/sm-perception-latency-15/offline-replay-numeric.json)。

| 同图各90次调用 | P50 / P95 / 最大耗时(ms) | 非空结果调用 / 非空图片 | 与完整搜索不一致 |
| --- | --- | --- | --- |
| 当前生产完整搜索 | 174.1595 / 492.6395 / 608.4636 | 18 / 6 | 基准 |
| 测试专用 `TryHarder=false` | 94.7341 / 143.1927 / 150.7497 | 3 / 1 | 15次 |

快速参数损失完整搜索可取得的候选，**不采用**，生产仍完整搜索。基准空结果72次中位183.5826ms、最大608.4636ms；有结果18次中位165.6403ms、最大234.9404ms。这里只按完整搜索结果分组，没有独立人工真值，不能将非空比例称为真实召回率。完整搜索→快速搜索固定顺序、含冷调用、普通Windows负载；这些耗时不用于跨版本性能提升或生产SLA结论。改进来自采集解码解耦与有界最新队列，以及经全部256灰度核对的纯常量帧快退，不能承诺任意空背景都可快速判空。

## 原生呈现计时与现场边界

最终原生测试保留4个合成观察的两阶段指标：读到→反馈合成37.3313–120.8751ms（中位73.0456ms）；读到→格口合成473.2044–1977.1144ms；快照发布→格口合成35.6594–80.3665ms（中位45.4000ms）。对应请求故意被夹具延迟/挂起，以证明即时反馈先于回包以及1500ms期限独立生效。样本很小、RCS为本机模拟，不能解释为真实RCS耗时/刷新率或物理屏幕测量。完整观察关联指标见原始 [WPF日志](evidence/sm-perception-latency-15/native-station-latency-ui.log)。确认中1366×768及Unknown紧凑1024×768原生截图已本机查看，截图留在D盘本地；800 DIP既有免滚动断言亦通过。

已从历史本机采图工程和受限配置定位30张3072×2048 PGM；仅在D盘私有目录重放，不上传图像、真实条码或其派生图。真实吞吐探针沿历史IP/序列号及原SDK目录实际尝试，19个导出和版本`0x04080201`核验成功，但未发现匹配相机，探针退出1、0帧、未Create/Open/Start；Initialize/Finalize及清理完成，保持设备未取得，不声称真实帧率、571ms稳定复现或曝光帧龄已测。仅公开 [安全摘要](evidence/sm-perception-latency-15/hardware-attempt-summary.json)，原始探针输出及实际身份保留本机私有目录。

最新帧策略可能跳过短暂出现、尚未解码的画面；它保证宿主队列有界和已识别事实不丢，不提供“物理包裹零漏扫”资格。真实停留时间/最坏解码、双相机、拔插、客户RCS、系统安装、长稳和现场仍须按对应设备/合同验收。旧安装介质不会自动更新；需发布包含本包SourceCommit的新介质后补现场记录，CI保持停用。

## 测试维护与运行

三份独立请求和三张原图的既有用例改为等待每次**识别/持久交付**后投入下一帧，仍阻塞图片写入并保留全部请求、像素身份、Unknown、停止和防重断言。旧“任何SDK借帧存在时都禁止解码”的夹具改为核验对应像素的借帧已经归还；并行持有另一帧是本轮允许的行为。新增阻塞解码压力专项单独验证最新槽、替换计数和同帧所有权。没有用删除业务断言或跳过套件获得PASS。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-pipeline
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --station-latency-ui
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-latency --private-pgm-directory <私有绝对目录> --repeats 3 --compare-fast-search --timing-output <全新D盘绝对JSON文件>
```
