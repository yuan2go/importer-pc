# 扫码调试闭环审查与软件验证 · SM-DEBUG-CLOSURE-07

2026-10-30；PR155。main为9df3901ec20d73512d48920b4671b529ebc58a9e，源码父提交为838e9acddcced110c3d4f61b190aff9a62e46be1；PR157原图/多码/计时改动已经在父提交中整合。历史01/02/04/05/06证据不倒写，本表为本次软件验证范围。

## 可复现缺陷和修复

| 编号 | 实际缺陷 | 修复与边界 |
|---|---|---|
| CL07-01 | 能锁码/图，但独立调试意图缺少Acquisition，内置RCS拒绝准备冻结JSON | MVS采集者对同帧诊断与观察传递同一个真实AcquisitionIdentity；Application核对设备、epoch、事件、时间、会话后关联独立DebugRequest意图。原隔离观察仍Blocked；未生成虚构采集身份或放宽RCS校验。缺失身份保留诊断图但不建请求 |
| CL07-02 | 权威读取失败后finally刷新按钮又用旧CanSend启用发送 | 本窗记录权威读取状态；失败、取消、选择/生命周期变化关闭发送，当前权威轮次及有效宿主快照恢复后才开放；服务端执行守卫仍最终裁决 |
| CL07-03 | 双窗抢先开轮后，旧CAS命令会一直重用，无法明确再扫码；进入回执丢失也可能停留核对标签 | 新增仅在持久接纳之前抛出的CameraReadRoundConflictException。UI只清退这种明确未接纳的命令，由下一次明确点击按新基准触发；网络/SDK不确定失败保留原ID。已确认的活动单轮会话结清旧begin核对，不再重复建会话 |
| CL07-04 | 800×420固定头部挤占操作区，发送动作无法有效到达 | 说明、相机选择和扫码/请求共同纵向滚动；原生BringIntoView断言实际发送按钮落在客户区，截图前后对照保留 |

InspectedFrame新增可选init Acquisition属性，null序列化省略，既有构造/解构保留；正常MVS观察ID/Admission公式、ObservedAt时钟边界及原帧池Publish身份不变，复用同一身份对象。WPF验证friend仅供既有测试程序集访问内部驱动，不增加运行时装配或生产外置资格。

## 所有权与审查结论

Application拥有唯一当前轮次、命令CAS、封存后台、持久隔离和发送意图。原采集线程执行缓存切换及SDK缓冲归还；赢家在回调中关闭解码门，再交后台落图/冻结请求。WPF只持有投影、选择、焦点和本窗等待；切页/关辅助窗口不创建第二个相机或发送器。

测试逐像素/哈希核对对比度处理前原图，比较真实ZXing四角与WPF绿色框，并核对PDA式后续帧/另一相机不能覆盖。DebugRequest保留真实源采集身份，但其Admission与原Blocked观察不同，没有把观察重新生产准入。只有明确发送可临时授权原T1，T2一次尝试先持久，原回包/T3与Unknown责任沿原机制。重复命令仅核对，改ID/摘要不能重发已尝试请求。

撤权中断在途HTTP形成Unknown；重新授权、再次扫码、清场、晚回包和重启均不把旧尝试变成未发送，恢复后先保持隔离且不恢复自动轮次。缺图/迟到图不借实时图补配；切换选择后迟到原图不重绘，多窗口旧图保留而旧发送关闭。

## 本次通过（全部合成依赖、实际软件代码）

| 命令/验证层 | PASS断言 | 证明范围 |
|---|---:|---|
| Application --camera-rounds | 36 | 实际MVS采集+真实ZXing+SQLite+内置RCS+本机HTTP；NoRead、多码继续、唯一赢家、同帧原始像素/坐标/身份、冻结JSON、Accepted/Rejected、单次发送、旧命令/摘要拒绝、再次扫码及未发意图取消、撤权Unknown/重启隔离/不回放、SDK切换取消/失败、缺失采集身份拒发 |
| 原生WPF --camera-round-ui | 19 | 实际页面按钮、失去begin回执同会话核对、真实像素/坐标/只读JSON、读取失败/取消关发送、双窗保旧图和CAS重试、迟到图代次、125%DPI的1:1、关辅助窗不换采集者、小窗发送可达、在途撤权清敏感显示并留Unknown |
| --sm-original-frame | 26 | PR157原始PNG留档、异步写入、停止/原像素租约、备份/退役及原Unknown不被图像更新覆盖 |
| --mvs-software-flow | 15 | 真实ZXing普通业务链路的帧身份/三个独立请求/原图租约、预览及设置拒绝，验证本次身份传递没有改变正常业务路径 |
| Probe --mvs-self-test | 73 | 合成原生端口的SDK生命周期、缓冲归还/资源、物理身份/连续采集、计时/跳帧及原图机制 |

Application验证、WPF验证、Probe和Sportmaster Desktop构建均0警告/0错误。原生1366×768/1024×768/800×420截图已查看；小窗按纵向滚动访问，不宣称全部内容同时铺满首屏。最终通过日志为app-run-accepted.log、ui-native-final.log、original-frame-regression.log、mvs-flow-regression.log、mvs-self-test.log；其他迭代保留形成时身份，不累计早期PASS。

## 失败原记录与夹具修正

全部迭代日志保留：真实请求准备失败、读取失败误开按钮、CAS重试卡住及旧小窗截图；还有夹具初稿的字典接口/Path/字段声明编译错误、解码完成计数同步、撤权后必须重新授权才读取JSON、SDK故意IOException的预期类型、WPF Owner必须先显示等。没有删断言或吞异常换PASS。

合成RCS回包初稿漏了四个必需的nullable测量字段，适配器正确形成Unknown；早期“Contains Accepted”不足以判成功，已加强为阶段精确Accepted并核对持久事实，补齐合成回包后重新通过。早期数字不作为Accepted通过证据。

双窗原生夹具额外的直接轮询会碰到四读者限额；最终控制宿主定时器，按真实按钮/快照顺序驱动，多窗同时持有同一Application。只对明确的读取背压进行有界等候，不重放发送命令，也不提高生产配额。本次不是无限多窗口并发/长稳资格。

## PR157保全及剩余边界

原图池、后台PNG/备份/退役核心、SIMD、原生SDK字节/ABI声明及原请求/回执核心未被覆盖；本次MVS只增加诊断帧真实采集身份的传递并复用原身份对象，另加测试friend。正常MVS+原图专项和Probe实际重跑如上，既有D/RV及旧备份三项失败不改写，本次未做无关全量备份套件。

仍NOT_RUN：真实双相机/客户RCS/真实PDA、SDK缓存与网络在传输帧的现场边界、插拔重连/目标机SDK兼容、安装发布冒烟、长稳/高速资源容量、权限服务与受保护真实部署、现场清场及物理责任验收。本次没有安装或宣称可投产。普通作业手工补录与内置RCS的独立来源合同不由本次隔离调试证明；06参考对端通过不能扩张为该链路验收。

## 运行

PowerShell仓库根先执行`. ./scripts/project-environment.ps1`，保持临时/缓存/运行数据在D盘。只在合成模式执行：

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll D:/projects/importer-pc-ui-debug --camera-rounds
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc-ui-debug --camera-round-ui
```

相关回归参数：--sm-original-frame、--mvs-software-flow；Probe --mvs-self-test。日志、截图、源码清单和哈希见[evidence/sm-debug-closure-07](evidence/sm-debug-closure-07)，不附私有SDK、字体字节、运行DB或客户数据。
