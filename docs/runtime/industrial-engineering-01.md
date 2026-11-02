# IE01实施与验证

2026-11-03，实施基线main `38cb7dc0`，设计提交`8d3f6616`。用户授权分阶段实现、必要验证和main PR；不合并、不操作当前生产部署。完整设计与23项验收定义见[工作包](../work-packages/WP-IPC-INDUSTRIAL-ENGINEERING-01.md)。本页持续更新实际状态，历史设计交付状态不倒写。

## 当前阶段

| 切片 | 实际状态 | 未完成的验收 |
|---|---|---|
| P01安全恢复 | 工程实现及核心回归通过：固定验证器已移除；旧API/RPC明确拒绝；v3受保护管道；一次性本站确认；Windows管理员服务端核验；完整会话/委托撤销；丢回执核对；发布安全代次 | 真实Windows令牌/UAC、同源安装服务往返、混版/重装/恢复系统矩阵未完成 |
| P02生命周期 | 实施中：独立可信维护缓存/原库存、Windows管理员卸载与备份选择、静默前端、语义化版本防降级、原安装未决防重；后续回退/版本清理/永久删除仍未完成 | 同源最终EXE、隔离VM、SCM/重启/退役矩阵未完成 |
| P03安装/首配 | 待实施 | 原生操作/连接测试/真实设备分层证据未完成 |
| P04故障/诊断/手册 | 实施中：两张一页卡文案、实施手册、5个CP码目录、README/分类INDEX/生成工作包导航及随包映射；轻量帮助/诊断包尚未实现 | 导出隐私及原生/打印QA未完成 |
| P05标准测试/CI | 实施中：MSTest/VSTest发现入口、锁文件、PR/main自动工作流和三装配构建；实际PR中7组核心回归、三装配构建及文档门禁通过 | 分支必需检查配置、覆盖率和全量格式门禁未完成 |
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


## 分阶段提交与当前环境限制

- P01提交`39a4de41`，main草稿PR [#184](https://github.com/yuan2go/importer-pc/pull/184)。草稿表示整包仍在实施，不代表所有23项验收已通过。
- P02初次安装器构建使用了不匹配的普通/自包含属性，触发NETSDK1150/NU1004；随后沿实际发布的win-x64/自包含/逐业务锁文件参数构建，发现静默回调缺Windows平台守卫（CA1416），已补实际守卫，未关闭分析器。
- P05使用官方NuGet的MSTest Framework/Adapter 4.5.2及Test SDK 18.0.1，标准入口已实际发现6组并执行：5通过，存储基线失败。失败原因为旧DB11断言漏掉已批准的DB12班次/事实表与索引；正在保留旧核心schema逐项相等断言的同时补精确DB12对象/身份检查，不删除断言或跳过该组。另新增生命周期边界夹具，当前目录应发现7组。
- 2026-11-03本机D盘耗尽，新增测试文件一度为0字节；已恢复源码内容。自动审批审查两次拒绝仅本工作树bin目录删除，原因仅返回“blocked by policy”；没有通过其他删除机制绕过。对本工作树可重建bin使用NTFS压缩取得约30MiB缓冲，压缩不改文件内容，尚不足以完成后续大日志回归/发布。已请求用户释放至少5GB；后续必要验证可由隔离CI继续，未完成项不能写本机PASS。

- PR自动CI首轮41505061149在Windows历史aux.js路径checkout失败；补继承仓库NTFS/长路径策略后，41505300469三个业务桌面构建全部通过，核心7组中6通过1失败，失败为精确DB12目录遗漏3个已存在的事实触发器；已保留并补齐这3个名称，未删除或弱化不变性检查。下一轮结果另记。
- 文档层已核对8个入口、226个本地链接及全部5个运行源码CP码；现场卡/手册仍为文案源，打印与原生/现场未验。随包映射只给Sportmaster增加新指南，不把SM说明套给其他业务。轻量帮助、诊断zip、连接检查/模板、程序回退/版本清理/永久删除及正式发布仍未完成。

- CI41506901684三个装配构建与文档门禁通过，核心仍为6/7：补齐20个DB12统计/班次对象后，旧“缺reply身份表”用例的特定错误短语过滤失败。当前DB12更早执行完整schema验证并已拒绝损坏库；测试改为验证InvalidDataException、原db字节完全不变且不创建receipts目录，保留拒绝/原件保护的实质断言，不要求必须走较弱的旧晚期分支。同时显式UTF8输出以保留中文失败原因。

- [CI41507490063](https://github.com/yuan2go/importer-pc/actions/runs/41507490063)，SourceCommit `2c8d3644ff19afc959948e2cf0b2baa7d1c03e46`：7组标准核心回归全部通过，Sportmaster/DirectPlc/WcsToPlc三套Windows桌面构建和文档检查通过。这是GitHub Windows运行器的工程证据，不包含UAC/SCM、真实SDK、最终安装包生命周期或现场验收。
- 随后维护缓存复查发现：通用受保护写入默认只授予运行用户读取权限，缓存EXE也需执行权限。已使缓存介质文件与正常发布文件一致，授予运行用户ReadAndExecute，保持无写权限；原安装库存仍只读。此改动需后续CI构建，普通用户实际调用缓存EXE/ARP仍待隔离Windows系统验证。
