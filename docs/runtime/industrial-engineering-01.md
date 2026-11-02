# IE01实施与验证

2026-11-03，实施基线main `38cb7dc0`，设计提交`8d3f6616`。用户授权分阶段实现、必要验证和main PR；不合并、不操作当前生产部署。完整设计与23项验收定义见[工作包](../work-packages/WP-IPC-INDUSTRIAL-ENGINEERING-01.md)。本页持续更新实际状态，历史设计交付状态不倒写。

## 当前阶段

| 切片 | 实际状态 | 未完成的验收 |
|---|---|---|
| P01安全恢复 | 工程实现及核心回归通过：固定验证器已移除；旧API/RPC明确拒绝；v3受保护管道；一次性本站确认；Windows管理员服务端核验；完整会话/委托撤销；丢回执核对；发布安全代次 | 真实Windows令牌/UAC、同源安装服务往返、混版/重装/恢复系统矩阵未完成 |
| P02生命周期 | 待实施 | 同源最终EXE、隔离VM、SCM/重启/退役矩阵未完成 |
| P03安装/首配 | 待实施 | 原生操作/连接测试/真实设备分层证据未完成 |
| P04故障/诊断/手册 | 待实施 | 导出隐私及原生/打印QA未完成 |
| P05标准测试/CI | 待实施；本轮已获恢复自动CI的授权 | 实际PR必需检查未完成 |
| P06正式发布 | 待实施；正式发布者/证书未提供 | 不发布未签名候选为正式交付 |
| P07企业打包/更新提示 | 待实施技术验证/延期能力按设计分别交付 | 不宣称MSI/MSIX已支持 |

## P01行为与运行方式

忘记管理密码：原登录窗口→“忘记管理密码？”→输入/确认新密码→Windows授权→本站重置确认。无需根密码，不自动进入管理模式。普通进程不能通过RPC宣称自己是管理员；服务从实际管道调用者令牌、PID/创建时间和Windows会话取得身份，远程管道拒绝。确认2分钟有效，绑定本站及凭据修订。密码或服务会话变更使旧确认失效。

失败后先点“核对上次重置”。原挑战保留在发起进程，服务只报告原OperationId的已提交事实；未完成且挑战仍有效时不发第二个重置；失效后原挑战不能再提交，才允许重新发起。新密码不进入命令行/日志。独立提权通道继续使用现有受保护请求及进程绑定机制。

管理凭据仍为schema2并新增凭据修订/恢复记录，管道升级v3；安装器/客户端/helper/服务须同源更新。旧v2服务无法授权新v3客户端，缺服务失败关闭；不创建可写本地fallback。历史共享口令与验证器退出运行源码和当前测试，历史归档/提交不重写。

发布声明增加`RecoverySecurityEpoch=1`，当前安装和管理服务准备拒绝缺失/低于安全下限的介质；此检查是当前工具的安全下限，不使历史安装器自动升级。旧安装器/裸程序的执行仍需渠道撤回和现场系统允许名单，Windows管理员完全控制OS的边界不变。

本地开发验证先执行`. ./scripts/project-environment.ps1`，再运行：

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --industrial-recovery
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --station-single-camera
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --management-mode
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1
```

### 已执行与发现

- Windows/.NET10.0.402：Application验证项目、SM桌面及原生UI验证项目构建通过，0警告/0错误；构建不等于原生交互验收。
- `--industrial-recovery`22项核心测试通过：真实慢哈希、普通/跨会话/伪造/过期确认拒绝、并发凭据变化、防重复提交、旧授权撤销及介质安全下限。核心测试显式传入合成Windows身份，不代表真实令牌或UAC通过。
- `--station-single-camera`回环RCS与恢复集成通过；保留原请求及Unknown的唯一发送。发现旧测试错误地用综合`Admit`断言相机来源不可用，按当前人工独立合同改成`BarcodeInput.Camera=false`且拒绝合成相机注入；双相机未配置仍验证人工能力，人工实际提交由原独立夹具覆盖，不删除设备拒绝断言。
- 管理回归首轮在长日志文本脱敏处触发Regex超时；没有提高超时换PASS。改为无回溯表达式，URI凭据另做线性匹配；若仍超预算，整个诊断片段显式隐藏，绝不返回未遮盖原文。原权威请求/回包字节不参与诊断遮盖。修正后完整管理回归通过，日志查询/遮盖/撤权/导出及原请求守卫均保留。
- 中间一次桌面构建因显式Compile清单遗漏新partial文件失败；已补充项目清单并重新构建通过，保留这条失败来源。

## 外部与现场边界

尚无正式公司发布者/证书、授权设备/真实相机参数及客户RCS测试接口输入；真实设备、现场托盘节奏及新人30分钟旅程NOT_RUN。系统级破坏性验证只允许隔离合成身份/数据根，不使用当前用户部署代替VM。模拟/回环只能证明相应代码与协议边界，不能算真实SDK、业务接受或物理投递验收。
