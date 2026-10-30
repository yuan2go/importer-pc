# SM-UI-DEBUG-02 后续验证计划（全部NOT_RUN）

2026-10-30。用户要求先实现，后验证。本文件定义后续应执行与留存的证据，不表示已运行或通过。01历史18项调试/54项MVS等证据保留原来源，不转记为02结果。

| 编号 | 后续场景 | 必须核对的事实 |
|---|---|---|
| DBG02-01 | 同源构建与接口闭包 | SM桌面、共享Application及两个验证工程编译；新增可选契约、默认解码适配、原生入口及依赖；无SDK/字体/技能工具误入发布闭包。 |
| DBG02-02 | 原帧与解码对应 | 合成单码、多码、无码、旋转、低对比、超限；原始像素hash、四角、候选和观察/事件/epoch/帧号严格对应；原图不被识别增强改写；截图与源PGM/PNG逐像素核对。 |
| DBG02-03 | 连续审计游标 | 超过可视500条、双相机交错、只一台有输入、空页、单条大候选、超过单页字节预算；顺/逆分页、追赶、取消后重读均无本相机输入被静默跳过；只读查询不占用业务写连接。 |
| DBG02-04 | 原结果持续跟踪 | 旧行从Prepared到Accepted/Rejected/Unknown/Conflict；跨驻留/归档、选择切换、迟到回包、更多可视行轮巡；不能把当前另一包结果借给旧记录，HTTP与业务接受分开。 |
| DBG02-05 | 隔离进入 | 管理/配置修订/实例改变、未确认停止真实投放、原在途未排空、部分相机离线或不支持边界；首个持久栅之后无新的测试码取格口请求。Preparing不冒充Active，进入失败不自动复产。 |
| DBG02-06 | SDK缓存切换 | 两相机OneByOne缓冲、解码中切换、缓存含测试码、取消后重试、SDK缓存清理失败、连接丢失、迟到/在传输中的帧；以当前SDK/设备实际语义证明最终切换边界；若不能证明，补齐守卫，禁止通过。 |
| DBG02-07 | 隔离结束与原请求 | 测试件未移除拒绝；清场确认后原生帧/SDK缓存/输入排空；隔离观察永久不建业务请求；旧未知请求不重发，旧T1报文/目标/合同hash不变；正常心跳不误计为测试业务请求。 |
| DBG02-08 | 撤权/期限/崩溃恢复 | 管理到期、主动退出、变更密码、时钟跳变、切页、停止采集、进程崩溃；隔离保持。原业务完整备份恢复但诊断集合缺失时仍恢复隔离；重新启动来源先有隔离标记，再获取帧。 |
| DBG02-09 | 资源与存储故障 | 队列满、128MiB预算、单帧/元数据/图片数/磁盘余量上限、写/rename/SQLite失败、索引或压缩件hash不符、原图轮替、临时/孤立文件；显式缺口、正确所有权与恢复，不能吞异常换成功。 |
| DBG02-10 | PNG/ZIP导出 | PNG像素/CRC及原件hash；ZIP条目和摘要一致；活动会话高水位、轮替图片计数、请求/回执预览脱敏/截断声明、取消与失败清理、已有目标拒绝、D盘/链接守卫、导出与保留期清理并发。 |
| DBG02-11 | Windows原生交互 | 标准/紧凑及高DPI，长内容、选择不跳、缩放/拖动/键盘、切页迟到、失权清空；暂停画面只冻结查看。旧ICameraDiagnostics投影夹具须适配新ICameraInspection能力，保留关键断言。 |
| DBG02-12 | 真实双相机与客户RCS | 获授权的实际IP、物理序列号和唯一编码；逐台断网/重连、服务不可达/拒绝/超时/迟到；实际RCS账核对测试请求数、正常请求数、原请求及接受事实。私有现场值只留D盘。 |
| DBG02-13 | 高速/长稳及现场恢复 | 以目标工控机、相机实际帧率、现场约定吞吐和运行时长为依据，记录CPU/内存/句柄、队列、落盘、WAL、帧缺口/轮替、输入与RCS账对账；不得编造通过阈值或用短时模拟替代长稳。 |

先执行环境脚本将构建和验证数据留在D盘。以下仅供后续阶段使用，本轮未执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
dotnet build tests/Ipc.Application.Verification -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -m:1 -nr:false -p:RestoreLockedMode=true
```

随后补齐02边界夹具并运行；不能直接把01 `--camera-debug` 的旧投影接口当作02完整验收。真实相机/客户RCS、系统安装与生产数据操作须在后续明确授权、实际具备SDK/合同/设备和现场条件后进行。本轮不运行任何上述步骤。
