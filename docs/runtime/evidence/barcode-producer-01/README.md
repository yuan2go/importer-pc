# BARCODE-PRODUCER-01 验证回执

源码/运行字节与原日志见 [verification.json](verification.json)。主体实现 `e6439f4a`，原生启动夹具及冻结验证入口 `7c9c17d7`。最终 Windows x64 介质的 SourceCommit 是 `7c9c17d7d79440fae08dd7bd68d491db57588799`；后续仅提交本回执及链接，不能将介质身份改写为该回执提交。日志中更早的工作树尝试保留原名，不扩张为最终源码通过。

本机 Windows 11 22631 x64，.NET 10.0.402。命令均先 `. ./scripts/project-environment.ps1`；数据、缓存、介质和证据均在 D 盘。最终完整 `dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false` 通过，0 警告、0 错误。

| 执行项 | 结果及防护风险 |
|---|---|
| Application `. --barcode-producer`，28 个共享黄金对话及真实 loopback/SQLite | PASS；握手身份/字段拒绝、缺口/重复/回退、旧 Epoch、触发连续性、独立心跳期限、物理租约保有、同码新实体、双流身份、默认观察及在途切换拒绝 |
| `. --p0-tcp`、`. --p0-tcp-components`、`. --dual-camera-communication` | PASS；原 plain 拆粘包/编码/上限/半行/FIN/RST/静默断连，取消 Stop 仍保有原任务，不能定时自造健康 |
| `. --deferred-connections`、`. --p0-deferred`、`. --parameter-guidance-startup`、`. --parameter-refactor` | PASS；保存校验与八种独立延后组合一致，唯一候选/Pending/Effective不变，不能由重连或页面编辑自动启用业务 |
| `. --p0-rcs`、`. --sportmaster-rcs`、`. --rcs-timeout-errors`、`. --station-single-camera` | PASS；同码原请求、Unknown/恢复/编号永久责任、单位置故障和超时后新实体行为继续受原守卫保护 |
| `. --architecture-quality-remediation`、`. --p0-provider-identity` | PASS；精确组件身份、资源生命周期及配置确认不会因更换默认 Reader 失效 |
| `. --hikrobot-sdk-editing`、`. --hikrobot-hardening` | PASS；MVS诊断仍可编辑；冲突身份拒绝；真实日志落盘/查询/导出均遮蔽条码和序列号，业务原件不改写 |
| Probe `--self-test` | PASS；原智能读码器46、MVS26、硬化16项，假原生端口；防止 SDK缓冲/所有权与机级租约回归，不代表厂商 SDK 通过 |
| Python `test_release_documents.py` / `test_application_compatibility.py` | PASS，3/16项；随包指南链接、来源身份及装配闭包不失真 |
| 原生 `. --barcode-producer` / `. --hikrobot-sdk-editing` / `. --parameter-guidance-startup` | PASS；真实 HWND/Dispatcher、首配共模、实体证据错误定位、左右隔离、滚动、D盘/窗口及延后预检 |
| 冻结 Windows 介质 `verify-commercial-wpf.ps1 -BarcodeProducer` / `-StartupExperience` | PASS；实际发布 DLL 的参数页、启动窗口、保存回读/收据未知、会话恢复与停止边界；前后复核介质完整性 |

发布命令：`python scripts/package-application.py --application sportmaster --target win-x64 --output .artifacts/barcode-producer/verification-final-win-x64 --verification-without-vendor-runtime`。此为明确省略厂商运行库的验证介质，不能投产；ZIP 不入 Git。随包包含当前运行说明、协议、设计和黄金对话，原旧相机通信回执留在原位置。

冻结参数截图：[日常](frozen-producer/producer-daily-sequenced.png)、[首配](frozen-producer/producer-first-sequenced.png)、[小窗证据字段](frozen-producer/producer-first-compact-evidence.png)。启动截图：[小窗审阅](frozen-startup/startup-small.png)。截图仅合成输入；实际目视复查过发布截图，没有以网页截图代替 WPF 验证。

原始失败没有删改：两次编译引用错误已修正；旧 Production 测试缺少实体证据/物理序列号时补充显式合成声明；旧测试试图在 Unknown 时延后读码源，被新守卫拒绝，现改为断言拒绝，并保留 RCS-only Pending、重启与原报文/次数不变的断言。早期原生截图暴露旧 SDK 路线提示，已纠正并在冻结截图重验。未删关键断言、吞异常或为通过放宽 Production。

未验证：真实相机/SDK、真实读码平台或自研服务、实际触发与完整解码结果、外部进程释放相机、断连期间实物处置、真实 RCS/物理完成、目标 IPC 长稳、安装/签名/生产资格。没有操作设备、安装部署或批准投产。运行合同仍只见[协议](../../../protocols/barcode-producer-v1.md)、[设计](../../../design/barcode-producer-01.md)与[运行说明](../../barcode-producer-01.md)。

裸 Windows CI 的原机级策略缺失失败及仅测试隔离修正见[补充回执](isolated-fixture-verification.json)。真实 ResourceLease 改在各次合成测试根使用；不再依赖开发机部署登记。生产代码/权限策略未改，barcode-producer、p0-deferred、deferred-connections 已在修正夹具上重新通过；原冻结介质仍保持上述 SourceCommit。
