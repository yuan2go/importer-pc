# SM感知时延与显示稳定性15

2026-11-01。范围及本轮决定见[工作包](../work-packages/WP-IPC-SM-PERCEPTION-LATENCY-15.md)。继承 PR167 / `5ae7bd59`，修改前当前指南见[原字节归档](../archive/pre-sm-perception-latency-15/manifest.json)。旧真机数据、原30张图、SDK、配置、账本和历史D/RV不改写。

## 四项实现

1. Presentation 为同位、同码、同连接 epoch 保存最近已确认格口，待新请求时最多1500ms显示为“上次格口 · 确认中，不能据此投递”。原 `Slot`、`CurrentTaskAvailable`、作业、请求和命令守卫不改变；波次/任务/SKU/数量全部退出。期限不能因刷新续期；拒绝、Unknown、不同码、来源/配置变化和失效不会借用旧值。快速反馈已先到而一致性快照仍是上一条成功时，也只作为上次结果显示，不冒充新请求接受。
2. 同一 MVS 所有者下分离专用采集和解码线程。SDK完整帧→托管复制→归还该借帧→单个最新待解码槽→原图副本/对比度/完整ZXing→独立观察/持久交付。最多1个处理、1个待解码和1个临时复制数组，不暴露原生指针；原图32帧/256MiB预算保持。替换仅发生在未解码视频，独立计数 `SupersededDecodeFrames`，不丢弃已识别输入；后者交付拒绝仍保留原观察并阻断重启。隔离切换清空未识别待处理帧并等待真实解码退出，再清SDK缓存；取消等待不伪称释放。
3. MVS4.8.2.1 本机头文件核对 `LatestImagesOnly=1`，软件识别配置此策略；严格采图/原始吞吐仍为 `OneByOne=0`。启动/重连核验IP、序列号与独占句柄；持续完整有效帧来自该句柄，可证明存活，稳定取帧时不做每秒全局枚举。无新帧时仍执行连接/身份/触发读回，SDK操作保持单一所有者串行。未取消坏帧、像素、ABI、身份或未交付输入守卫。SDK帧号间隔与宿主槽替换分别记录，不能视为网络丢包，更不能证明物理包裹不漏扫。
4. 同进程100ms轻量只读反馈显示“已读到”，短暂边框与最多每秒一次系统短音不表示准入/RCS接受。非重叠、最多每窗一次在途的合成测量：WPF Rendering→Dispatcher空闲提交→后台 `DwmFlush`→原观察身份回报。记录解码完成→反馈合成、解码完成→格口合成、快照发布→格口合成，最多512条，进入 `runtime-progress.json` 的 `Presentation`。它包含该次实际RCS等待，不用127.0.0.1数据替代客户往返，也不把它称为曝光/物理屏幕光子时延。

`DwmFlush` 的边界依据 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush)：等待本进程已排队的图形更新到下一次呈现；并非整个会话刷新。窗口隐藏、卸载、最小化、过期或身份变化时不记成功；合成失败单独保留为不可测。声音走系统声音配置，未声称实际现场扬声器验收。

## 计时与召回口径

SDK返回时的 `ObservedAt` 不挪动。新增 `host.decode-queue-wait`、逐帧 `DecodeQueueMs` 和未解码替换标记；复制、归还、解码、持久交付和最终清零仍分段记录。`ExposureFrameAge` 明确为不可测，直到目标设备时间戳频率、SDK主机时间戳单位和时基映射验证完成；不得将最后一次SDK返回距今的时间当曝光帧龄。

测试专用参数比较入口 `--mvs-latency --compare-fast-search` 同批同图比较完整搜索和 `TryHarder=false`，同时记录耗时和完整候选多重集合是否一致。生产保留全码制、旋转、反色、下采样和完整搜索，不因某批图更快便削减候选范围。确定无对比度的纯常量Mono8帧直接为空，不进入原生搜索；与真实完整ZXing逐一核对全部256灰度值，其他图像仍完整搜索。

## 已执行与待验

精确源码指纹、命令、原始正负日志和统计见本轮 `evidence/sm-perception-latency-15/summary.json`。构建、合成端口、原生Windows、离线实图、真实SDK及安装分别记录。

已从历史本机采图工程和受限配置定位30张3072×2048 PGM；仅在D盘私有目录重放，不上传图像、真实条码或其派生图。真实吞吐探针沿历史IP/序列号及原SDK目录实际尝试，导出和版本核验成功，但未发现匹配相机，0帧、未Create/Open/Start；保持设备未取得，不声称真实帧率、571ms稳定复现或曝光帧龄已测。

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
