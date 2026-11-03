# CQ03 四项整改结果

基线main `ec698bd4e11d06c13c097c77f90cc5ce145ab3ea`。用户要求修复CQ03-01～04并先修文档；文档先行提交 `c70dee2`，源码与随包指南冻结于 `8eee4c61082e1c7eeda245d17a2202556d233eab`。原工作区客户资料未纳入，历史审查、失败、原候选及D/RV不改。范围见[工作包](../work-packages/WP-IPC-COMMERCIAL-QUALITY-FIX-03.md)，逐项命令选择器、退出码、原日志SHA及产物身份见[证据清单](evidence/commercial-quality-fix-03/verification.json)。

## 结论与实现

|问题|结果|实际修改及边界|
|---|---|---|
|CQ03-01 P1 当前件错位|通过|ParcelViewModel.Current/PositionRows按Positions.CurrentOperation及同位Operation.Id查找；supply-station无当前ID时也不回退历史。逆序/相同观察时间、缺失ID、错位、双窗和历史选择不再改变当前命令Owner。历史仍按时间排序。|
|CQ03-02 P2 旧快照Ready|通过|两布局共享ParcelSnapshotValidity，不再各存一套读取状态。失败或3秒未成功读取显示失效/最后成功时间，关闭依赖投影的业务及维护写入；VM再次守卫，迟到结果按读取代次处理。classic轮询即使读取挂起仍更新失效通知。独立Pause不依赖普通Busy，但仍要求应用读取及命令实际成功；不是硬件急停。|
|CQ03-03 P2 终态恢复标记|通过|ApplyReply权威主终结及受守卫AbandonUnsubmitted清RecoveryRequired。旧残留经冻结身份、原Committed回执/归档raw、物理完成或原取消审计证明后收敛；同事务保存原JSON/原SHA和修复审计。归档件保持归档，并保留旧归档版本。Unknown、PlcAccepted、冲突、缺主raw及仅有Abandoned标签仍拒绝旧库升级，原DB字节不变。独立缺raw/图像/上传/包责任不因标记清除消失。|
|CQ03-04 P2 维护文档冲突|通过|直接修正当前ABI为1.7.0，CLI DB2–5显式到6与旧API默认5分别表述；OutcomeReconciled→Awaiting属于SQL5，SQL6仅增加RCS语义。旧版本与Windows06候选改为历史身份读取。|

业务为1.2.5，公共契约API仍1.7.0，Parcel DB6及SQL1–6/checksum、配置schema1/2/3均不变。冻结构建的SourceRevisionId改变公共DLL精确字节，因此ProviderSets同步新不可变能力包2.1.3、reader1.2.3和RCS1.0.5。新包按实际Release公共DLL构建/签名/校验；旧包、请求、合同及目标不改。RCS协议行为未扩展。

旧数据升级仍先完整备份并保留匹配原发布/信任/包。普通升级只读预检仅接纳已被原证据证明终结的旧标记，随后写本地收敛审计；不发外部请求。缺依据继续隔离，不提供强清命令。离线消费者读取仍保守保留未收敛旧标记及其原包；需在受控维护根完成有证据的收敛，再检查退休条件。Effective仍绑定旧精确扩展时，先沿匹配旧发布清偿、暂存允许的完整候选并受控过渡，不能把新扩展装进旧版本号绕过指纹。

## 验证

macOS 26.5 / arm64，SDK10.0.402、Runtime10.0.13。三个新增基线用例分别失败后修复通过；既有断言未删改，版本测试仅同步新的发行号，原不兼容拒绝断言保留。

|验证|结果与范围|
|---|---|
|核心构建|通过，0 warning/error；`dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false`|
|定向验证宿主|通过：`--commercial-quality`、`--sportmaster-ui`、`--parcel-recovery-p2`、`--receipt-custody`、`--upgrade-conflict-closure`、`--configuration-confirmation`、`--architecture-quality`、`--version-compatibility`、`--sportmaster-rcs`、`--sportmaster-window-lifecycle`。新增CQ03组最终补验含三个Application的原Query终结及resident/archive DB5→6；UI组为真实VM加事件替身。|
|真实WPF入口|Sportmaster、DirectPlc、WcsToPlc跨构建通过。两个PLC入口另显式指定各自已入库NuGet lock路径，未保留首次通用命令生成的替代lock。Sportmaster另构建win-x64自包含实际发行包。|
|解压portable真实CLI|三个默认流程及WcsToPlc Attached通过；原进程重启和原Query后PhysicalCompleted、RecoveryRequired=false、PLC Attempts=1、上传Accepted。false依现有JSON契约省略，读取按默认false。|
|签名选装实际Release|能力/reader/RCS三个新包静态验收通过。解压媒介实际安装、完整CAS配置、双位reader+WCS、失败激活保旧、受控恢复、安全卸载通过；独立合成HTTP RCS验证原请求/原响应、双位、重启不重发及拆包检查后本地处置，并断言恢复标记false。|
|Windows媒介|win-x64 ZIP逐文件SHA、实际Sportmaster WPF程序集及检查器公共DLL字节一致性通过。仅属跨构建与媒介核验。|

首次新增测试编译错误、测试错误地期待Paused字符串、发行验证工具使用已清理临时源码目录的失败均保留，修正接口签名、契约读取或隔离执行路径后重跑；没有修改业务语义换取PASS。测试/构建的原命令与原日志在本机不可变证据目录，公开清单列原hash；新的选装验证工具见[本包脚本](evidence/commercial-quality-fix-03/selected-release-journey.py)，历史工具未改。

资料清单、链接及公开范围复核：系统Python缺Pillow的初次环境错误保留；改用现有含Pillow环境后，public archive整体仍FAIL，当前与原基线同为267项历史资产问题（265个PPTX媒体类型不匹配、2个旧元数据标记），新增0/删除0，见[差集](evidence/commercial-quality-fix-03/archive-error-delta.json)。本包文件无新增清单/链接错误，没有新增豁免或改写旧资产来换PASS。

## 交付和未验证边界

5个ZIP（三个portable核心、Sportmaster选装portable/win-x64）和3个新ipcx均来自上述冻结源码，SHA见清单；本次未上传测试签名私钥、客户资料或生产数据。最后证据/manifest提交与源码SHA的差异只属本包结果及资料清单，未改变运行闭包，不机械重跑历史矩阵。

源码整改和参考入口验收通过，可继续开发及参考实验室联调。Windows原生窗口/Dispatcher、焦点/DPI/触控、普通账户/UAC/ACL/安装维护为NOT_RUN；DirectPlc/WcsToPlc本轮未生成新的Windows发行ZIP，只有其真实入口跨构建及portable运行证据。真实SDK、设备、WCS/PLC合同及现场为BLOCKED/NOT_RUN，DirectPlc完整UI仍按原延期。指定场景商用交付仍不通过，不能据本包参考PASS放行。没有连接生产系统。
