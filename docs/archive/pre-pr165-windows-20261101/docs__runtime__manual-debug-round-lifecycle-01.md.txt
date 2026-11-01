# 人工调试轮次清理与容量修复

2026-10-31，基线 main `67ab87c1a1b146611504342c6ab080ed045231ec`，产品及回归源码提交 `fca2038021989aa283a667472aeafae19bed49e1`，完整代码树 `10e020fdce6b57ed187b482d89991c1a15225bf3`。源文件及原始合成日志的 SHA256/长度见[证据清单](evidence/manual-debug-round-lifecycle-01/manifest.json)。本轮只处理 PR162 的三个审查问题，不包含 PR161/163/164 的并行变更。

## 复现与修复

| 问题 | 修改前证据 | 最终行为 |
|---|---|---|
| P1：PrepareWcs失败残留 | 在未修改的main产品源码上，真实数字请求ID范围耗尽后持久化Blocked/Wcs=null；CloseUnsentDebugRoundAsync拒绝，回归失败 | 无WCS与Prepared/Attempts=0共用安全判定；审计取消后保留原记录，允许明确替换 |
| P1：离页异步竞态 | 源码控制流确认：离页后先return，未登记ownedManualRound，Deactivate亦已完成；原生竞态尚未运行 | 委托固定原命令；结果到达后同时核生命周期对象和代次。已离页/取消/重进/切位时，使用原CommandId和RuntimeId清理，不使用响应中的当前轮次或新快照身份 |
| P2：容量自阻塞 | 未修改main产品源码上，MaximumOperations=1准备成功后CanSend=false，回归失败 | 发送使用已保留名额；新准入仍检查容量，替换只扣除实际可取消的旧名额；存储余量与其他生产守卫保持 |

`CanCloseUnsentDebugIntent`要求所有阶段均未尝试、没有活动阶段和Pending回执、调试意图尚未结束。显式关闭还核对运行、轮次及操作证据。Accepted/Unknown/已尝试/Pending请求不改写、不重发。与本窗原扫码隔离相关的迟到结果也只按原会话＋原轮次申请关闭；冲突不能解除其他窗口的新轮次。

## 实际验证与边界

| 项目 | 结果 |
|---|---|
| 锁定.NET SDK 10.0.402，Release核心全量Rebuild | PASS，0警告/0错误 |
| 新增跨平台定向回归：真实SQLite＋内置RCS loopback，显式Simulation | PASS，18项检查；见final-rebuilt-lifecycle.log |
| 基线负向回归 | 两项真实失败：容量1无法发送、Wcs=null无法清理；Pending责任拒绝检查原本通过 |
| 修改文件Roslyn语法检查 | PASS；仅语法，不能代替WPF类型编译/运行 |
| WPF交叉编译 | BLOCKED：还原阶段NU1102（System.Diagnostics.EventLog 10.0.13）；未进入编译。禁用pruning及加入官方原包的临时诊断也失败，保留日志，未修改生产依赖或锁文件 |
| Production完整manual-supply/manual-rcs-debug、camera-rounds及原生manual-rcs-debug-ui | NOT_RUN。Linux上直接尝试Production被既有“Protected Production drafts require Windows”拒绝，未修改或绕过此保护 |
| Windows原生迟到准备结果回归 | 已增加，NOT_RUN：没有Windows/WPF交互桌面；覆盖持久化后离页、离页再进入、另一窗口替换新轮次 |
| 真实SDK、客户RCS、安装包、目标工控机及现场 | NOT_RUN |

18项包括容量1及普通新准入拒绝、同槽替换、旧窗口关闭拒绝、新轮次保全、单次发送/重复发送、Accepted与Unknown保全、ID真实耗尽、失败准备重放、无WCS清理及重复清理、失败意图替换、已完成清理回执对新轮次无影响、T3失败、原始回执确实durable pending T3、清理拒绝及原请求字节/尝试保全。未删除原11项manual-rcs-debug断言，原生产套件不借本轮Simulation通过记为PASS。

## 运行及补证

Linux软件合成验证（同源构建后）：

```sh
dotnet build tests/Ipc.Application.Verification -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --manual-rcs-debug-lifecycle
```

本机SDK在关闭workload resolver后完成核心构建；这只作用于命令环境，没有改仓库构建规则。首次Production尝试是环境拒绝，后续独立Simulation夹具才执行上述18项，不把二者混为一轮。最初基线/首轮14项与最终增加Unknown等检查后的18项日志分开保留。

Windows补证先按仓库规定使用D盘：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -m:1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1
dotnet run --project tests/Ipc.Application.Verification -c Release --no-build -- . --manual-rcs-debug-lifecycle
dotnet run --project tests/Ipc.Application.Verification -c Release --no-build -- . --manual-supply
dotnet run --project tests/Ipc.Application.Verification -c Release --no-build -- . --manual-rcs-debug
dotnet run --project tests/Ipc.Application.Verification -c Release --no-build -- . --camera-rounds
dotnet run --project tests/Ipc.Desktop.UiVerification -c Release --no-build -- . --manual-rcs-debug-ui
```

补证前保持草稿PR，原PR162审查线程不标记已解决，不合入main。依据AGENTS的“按风险执行构建、受影响模块、原请求/防重/恢复/资源验证”及“禁止……跳过关键测试”，不能以语法检查、Simulation通过或历史Windows PASS替代本次离页竞态的必要原生验证。CI保持停用，不调度Actions。

## 文档与公开范围

未发现现行12合同需要废除的内容；本轮不删生产逻辑或测试。原索引先按字节归档，manifest保存来源提交、Git Blob、SHA256及长度，再增加本包入口。历史设计、D/RV与验证记录不倒写。本轮只提交通用代码、定向合成回归、文本日志及哈希；不提交数据库、SDK、安装包、真实相机/客户数据或凭据。
