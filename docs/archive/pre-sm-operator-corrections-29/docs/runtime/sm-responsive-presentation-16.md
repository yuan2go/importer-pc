> 2026-11-02 [SM-UNREAD-PACKAGE-27](sm-unread-package-27.md) 补充默认识别无候选时的受限固定阈值搜索，以及无码/坏帧和重连的当前呈现失效水位。原像素、完整候选、多码拒绝、源时效与原请求责任不变。下文相应旧说明保持形成时范围；[原字节归档](../archive/pre-sm-unread-package-27/manifest.json)。

> 2026-11-01 合并前全会话复核和边界补修见 [SM-SESSION-REVIEW-17](sm-session-review-17.md)。本页456项及对比度补证保持各自原源；17不倒写这些历史结果。

# SM即时反馈与主动呈现16

2026-11-01，承接PR167及产品/证据源 `5aa8c890`，按[工作包](../work-packages/WP-IPC-SM-RESPONSIVE-PRESENTATION-16.md)实施。用户最新要求明确废止15的同码1500ms旧格口保留策略；15原始指南字节见[归档](../archive/pre-sm-responsive-presentation-16/manifest.json)，形成时结果不改写。当前产品仍为内置MVS＋进程内软件识别和内置RCS，无PLC。

## 当前行为与责任

1. SQLite WAL及`synchronous=FULL`不变，原始输入必须完成持久交付后才进入业务处理。MVS解码完成→`RecordPresentationRead`→只读呈现订阅在同步持久化之前发生，既不等待DB也不取得业务门锁。键盘/PDA通过原命令/CAS/准入检查后建立观察，同样在原命令提交前发布读到反馈。它只表示识别/观察已发生，不表示持久接收、准入、RCS接受或物理完成。
2. 作业页每位保留相机/扫描/任务三组。读到时显示码尾、220ms边框及限频系统短音；格口位置为灰色“处理中…”，旧格口和旧任务字段立即退出，包括相同条码的新观察。只在本次权威输入/作业/请求回应匹配时显示原超大格口和任务字段。拒绝为红色原因及不同系统声音；Unknown仍保留原责任及原指令，不被提示覆盖。系统声音配置和现场扬声器不由模拟验证保证。
3. `IParcelPresentationSource`是专用类型化只读订阅，不是万能事件总线。业务事务提交后写入一个容量1的变更信号，唯一后台发布者在原业务门锁下取得一致性快照。每订阅只有一个最新邮箱，最多8个订阅，撤销即释放容量；不在采集/事务线程调用消费者。快照只在发布时间变化时交付，读取/失联更新不重复反序列化整个业务图。内部共享不可变JSON字符串，各订阅取得独立数组对象，不能修改权威状态或其他窗口。
4. UI不再以500ms轮询驱动实际Runtime业务状态，也移除100ms“已读到”轮询。订阅泵合并到一个Render优先级回调，快读到/失联状态不等待慢的一致性快照应用。快照最多一个应用中、一个最新待应用；焦点、草稿、选择和准入不受合并影响。重复同观察按runtime/device/epoch/observation身份合并；不同观察即便同码仍独立持久/发送。边框与声音限频抑制连续重复动画，不以码字符串推断实体。
5. 驱动确认的相机状态变化先经独立短锁广播，再记录诊断；左位失联通知立即隐藏左位旧格口并禁用本位相机准入显示，右位独立。此处不承诺物理断线到驱动确认的检测时间。恢复事件本身不能恢复业务，必须等同epoch且时间不早于通知的一致性状态确认；该快保护也不能覆盖后续更可靠的源确认。已允许的手工补录仍沿原手工准入合同，不把相机离线改成全站阻断。
6. Application每秒重建时间敏感投影，UI仍保留500ms本地新鲜度/提示期限检查；两者用于权限/确认过期及静默故障，不是正常输入的轮询送达。超过3秒的首次/迟到推送不能重新续期旧状态；来源结束/失败使旧投影失效。存储致命故障保持原失败和原请求责任，并即时发出失效信号。停止等真实发布者退出，撤销所有邮箱，再排空读取及数据库；诊断DWM fence仍不阻塞业务关闭。

## 已有调试视频

实时预览已存在于管理模式调试页，独立于本轮锁定照片。保持每台相机既有采集/解码所有者和一个最新预览，采集端降采样至最大640×480；全分辨率仍只用于原图与识别。

采集端缩略图/原生扫描行使用ArrayPool，预览消费者复制到独立池化借帧，最多8份在途；新图、清屏或停止不能覆盖未归还借帧。有效像素仅为Width×Height前缀，池尾清零，归还幂等且归还后Frame拒绝读取。取消、换源、权限撤销、失败和离页的`using`路径归还；源清理不虚构消费者已释放，但消费者只持有托管副本，不延长SDK借帧/设备租约。

新借帧应用端口只读取已冻结的来源映射、管理资格及既有托管缩略缓存，不取得业务门锁、不调用SDK或重建连接；仍受原读取并发/真实退出所有权管理。旧独立复制预览API保留给既有调用，当前WPF采用借帧端口。

预览源和UI调度目标上限15fps，不保证现场最低帧率；同尺寸连续帧复用一个WriteableBitmap并在UI线程WritePixels，结束后才归还池缓冲。换源/失效清屏并丢弃位图；不新增视频编码、全分辨率传图或外部依赖。依据[Microsoft WritePixels](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.writeablebitmap.writepixels?view=windowsdesktop-10.0)及[WPF线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)核对API，采用有界异步调度，不同步阻塞Dispatcher。

## 验证与交付边界

完整19套件验证源码为 `1f663d69f4debb6e001343fcfca65886fa6b6408`，产品实现提交 `81a778b6`；两者src产品树相同，1f仅修正回归夹具的投帧边界并澄清文档。源码干净提交后重建三个验证工程及Sportmaster Desktop Release，均0警告、0错误。19个定向套件共456条断言PASS：Probe106、Application211、原生WPF139。原始日志、复现命令、构建身份、源码/证据指纹见 [summary.json](evidence/sm-responsive-presentation-16/summary.json)。数量为断言数，不是现场案例数。

| 本轮重点 | 最终证据 |
| --- | --- |
| 持久前反馈、旧目的地退出、同观察去抖、左右独立失联 | 原生专项13条PASS；真实预持久入口挂起时没有新HTTP请求，已出现码尾/灰占位且取得DWM指标；另一位草稿、选区与焦点保持 |
| 订阅有界和失效 | 第9订阅拒绝，撤销释放容量；单订阅并发读取拒绝，取消结束真实等待；慢订阅直接拿最新状态，数组修改不影响其他订阅；过期首次推送不能续期 |
| 呈现分段 | 4个故意延迟的本机模拟RCS观察：读到→反馈合成9.8024–44.0213ms；快照→格口合成27.0683–42.9311ms。样本很小，无生产SLA或跨版本提速百分比资格 |
| 持久前用例时间 | 从合成投帧到夹具观察到DWM指标的上界83.589ms，含识别/派发及夹具轮询；证明不等提交，不是fsync时延或物理屏幕光子测量 |
| 池化/位图 | 2000次预热借还分配400000字节元数据，单张有效缩略像素272640字节；8份在途限制、未归还不覆盖、清屏仍保有合法借帧、重复归还及归还后读取检查PASS；原生跨帧复用同一WriteableBitmap |
| 原合同回归 | 87条MVS生命周期、阻塞解码最新槽、双相机预览调度、原图像素/坐标、冻结RCS字节/防重/Unknown、退出、离线恢复、PDA连续输入、标准/紧凑布局均按对应日志PASS |

1366×768提交前反馈/离线拒绝及1024×768 Unknown原生截图已本机查看，800×420原有免滚动断言亦通过；截图留D盘。WPF截图、真实DWM fence和现场设备是不同验证层。

当前呈现源为 `31f69966c12d8334b0fcf4c40e74fe86a25d683a`：收口只把18 DIP灰占位前景色由`#68788E`改为现有中性文字`#536577`，背景`#F6F8FA`的数学对比度超过4.5；字号/布局、业务、采集、解码、数据库、请求及退出代码均未改变。该源追加两个Release构建0警告/错误，提交前反馈、分阶段/Unknown及标准/紧凑操作界面三套56条原生断言PASS。它们是456项中受影响部分的重跑，不累加成新案例数；精确源差异及原始日志见[对比度补证](evidence/sm-responsive-presentation-16/contrast/summary.json)。

保留四份中间FAIL日志，不计入最终PASS：早期即时反馈超时；旧夹具把“结果框不可见”写死、未容纳新灰占位；旧轮次夹具连续塞4/5帧却要求未开始解码的第4帧必胜；PDA首轮前台HWND不符。轮次原记录确认锁定了合法最新帧5，最终夹具在实际锁定4后才投入下一包5，全部帧号/原图/资格/冻结/防重断言保留；独立压力专项仍验证尚未解码可替换。PDA守卫在向错误窗口发键前中止，保持源码/断言不变的隔离重跑35条PASS；没有放宽前台校验或跳过关键测试。早期反馈超时原日志没有完整字段状态，不据它单独归因所有时延问题。

测试挂起的是现有真实持久化入口的前置边界，证明UI不依赖完成提交；它不是目标工控机fsync硬件测量。呈现时延仍是同进程输入就绪到DWM fence，不是曝光到物理屏幕光子。未重新宣称低端工控机磁盘、真实双相机、客户RCS、物理声音、安装升级或现场验收通过。15的30图参数实验及真实SDK未匹配相机事实仍保持各自SourceCommit；本包未改变ZXing搜索参数，原图不上传。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --station-responsive-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --station-latency-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --camera-preview-lifecycle
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-preview-pool
```

CI保持停用；业务变更继续在原开发PR167提交，未授权代合并/安装。真实部署需构建包含本包源码的新介质；已安装旧版本不会自动更新。用户原工作区、真实配置/图像/SDK及历史证据不动。
