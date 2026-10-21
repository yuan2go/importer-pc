# OPS-ADMIN-LOGS-01 · 交付验证

2026-10-21，开发分支 `codex/operator-admin-logs`，[PR86](https://github.com/yuan2go/importer-pc/pull/86)。开工及最终复核 main 均为 `3b883fe6a53b58b29eeca18c3719b5bfc0ee388e`；PR82～85仍open（84/85 draft）。整合85 `d52ade7` 与84 `587fbe8`，最终核对这四个PR的head均无后续源码差异，保留原PR和历史SourceCommit。最终产品为Sportmaster 1.10.1 / Contracts 1.15.0 / 配置schema3 / DB8；1.9.x、1.10.0及1.10.1早期冻结保留中间候选身份。

功能取舍见[工作包](../../work-packages/WP-IPC-OPERATOR-ADMIN-LOGS-01.md)，实际操作和安全边界见[当前指南](../operator-administrator-logs-01.md)。删除支持摘要的契约与实现，保留独立诊断、备份恢复和原业务审计。本轮不改写历史D/RV。

## 最终交付与本机D盘迁移

推荐本机安装器 `CainiaoSupplyStation_SM_Setup_v1.10.1_win-x64.exe`，产品及固定原生夹具源码均为 **`674807df21087f992663469e6ead94ae9a4f9c8b`**。本机交付目录为 `D:/01_工作/供包台/交付/SM1.10.1-674807df/`，仅一个EXE且复制后hash核对一致；原构建留在 `.artifacts/ops-installer-1.10.1-ack/`，同源ZIP、库存与发布记录位于 `.artifacts/ops-release-1101-ack/`。后继 `8917115` 只修正CI证据目录，src/config无差异；本报告等收口提交也不重标介质。

| 本机同源产物 | SHA-256 |
|---|---|
| 单文件安装器 | `1266f557b8c05f1d7f8e0ec536b0df56132d670eef8e14a7cdcf37055041b8e8` |
| win-x64 ZIP，738项库存 | `b8a4ebca0f74f9284d3687238796147734013ccf2d6876204f2fc68025d98621` |
| portable ZIP，112项库存 | `b401dd45c03b71a4f51b59e39271b2a1bb7e9d18dd8f70039b90930c5f8038be` |

本机冻结DLL的管理员原生17项、首配93项通过，全部738项介质hash保持不变；当前关键截图均来自该冻结DLL。CI [run75](https://github.com/yuan2go/importer-pc/actions/runs/39619539493) 全部通过，其合并测试源码为 **`0de2e63a4505da8a2b25b8252d388c6a4465515e`**，与本机源码分别记录。CI真实SCM/System宿主及受限Windows令牌29条通过记录，含64次管道复用、密码登录、凭据读写/删除拒绝、首管批准、失败锁定、撤权、一次性票据及服务缺失拒绝。修正服务断开前未确认完整响应的真实竞争；客户端确认后才复用管道，不使用重试掩盖失败。

CI冻结核心122条通过记录（其中管理员/日志52）、商业原生27、参数V3 227、视觉108，发布政策、原生安装器同源DLL核对和受控清理检查均通过。[CI可安装产物](https://github.com/yuan2go/importer-pc/actions/runs/39619539493/artifacts/12353194633)保留至2026-11-20，其SourceCommit保持上述CI合并树，不替代本机产物身份。

公开归档按规范Git字节与最新main比较，均保留298项历史问题，本轮新增0；校验器原始状态仍为FAIL，未放宽规则。最终输入树及原报告hash见[本轮比较](../evidence/operator-administrator-logs-01/archive-comparison-final.json)，此前1.9.0比较回执另行保留。

用户追加“全部项目内容移到D盘，包括已安装程序和运行数据”已实际执行。74个遗留构建/验证目录、NuGet与dotnet开发缓存、5份仓库外私有原稿迁至D盘。1,658个原安装/配置/用户文件约485MB逐项内容和ACL核对后完成迁移，原件全部在D盘受保护回退目录；原始请求、配置账、DPAPI身份与草稿不改写。使用同源冻结介质完成原责任预检、完整备份和安装升级。正式安装、配置、用户数据及System认证宿主/账号根均位于 `D:/ImporterPc.Deployment/`；4个Windows快捷方式和系统安装登记已更新。普通、无Codex包身份的Windows进程再次核对实际路径、服务及登记为PASS，已安装EXE的 `--environment-report` 退出0。没有启动业务或设备，也没有创建默认管理员账号。

迁移的首次目录数组格式失败、自动回退及MSIX路径隔离修复均保留在本机 `.artifacts/`。普通Windows环境已验证旧用户路径只作为D盘的兼容入口；受保护机器/账号路径直接使用D盘，不通过链接放宽守卫。仅系统登记和兼容入口元数据保留C盘。详见[D盘存储与运行](../d-drive-storage-20261021.md)。本次实际同机迁移/安装不扩大为完整安装器EXE交互、重启、双账号、换机恢复、真实SDK/RCS或现场验收通过。

## 本轮工程验证

全部为本轮组合源码执行，Windows原生截图来自HWND/Dispatcher及合成端口。不是旧版本测试，也不是浏览器/HTML对WPF的替代验证。

| 验证 | 结果与范围 |
|---|---|
| 核心 `--operator-administration` | 最终CI冻结52项PASS：真实密码哈希、首管不可覆盖、持久失败保护、退出/到期/账号撤销、一次性请求绑定helper授权、免登录管理员保护、参数保存守卫、轮转原始上下文、完整异常栈、307匹配/308时间记录全量导出、15000条流式导出与供包并发、正则超时/取消、未知防重、实际RCS无Query及Production缺保护拒绝。 |
| `--parameter-refactor` | 最终提交8917115重建夹具81项PASS，含继承的四类完整候选、下限错误定位与拒绝首样本重启后身份冲突；与本机发布src/config无差异。 |
| `--configuration-confirmation` | 41项PASS，暂停/排空、原确认身份、恢复与持久确认守卫；只对明确未准入状态有界等待，不吞异常。 |
| `--parcel-recovery-p2` | 101项PASS，持久原次Query证据、重启与原请求不重发。 |
| `--version-compatibility` | PASS，跨业务/篡改合同拒绝；原配置和Pending保留。 |
| 原生 `--operator-administration` | 本机冻结674807及CI分别17项PASS、8张截图：常规/紧凑角色导航、真实账号显示、左右自动供包、完整候选跨退出保留、真实软件日志/原始异常路径与堆栈、800×420查询与取消、撤权关闭已打开页面。 |
| 原生 `--parameter-v3` / `--visual-productization` | 最终CI冻结227 / 108项PASS，四类参数、保存核对、草稿/错误/焦点、条码与格口关联、800×420～1600×900客户区；此前219/102记录保留形成时身份。 |
| 原生 `--startup-experience` | 本机冻结674807的93项PASS，五步完整候选、TCP/RCS静态配置、未知保存防重、首配/安装窗口及退出/到期后原候选重新登录。认证账可以创建，业务DB和设备租约不能在向导浏览时创建。 |
| 受影响Release构建 | 核心验证、原生验证、Sportmaster Desktop均0警告/0错误。 |
| Python发布检查 | commercial_delivery 5、release_documents 2、release_build 1、unified_release 15项通过。首轮缺dotnet PATH及GBK解码失败保留，配置真实SDK路径、UTF-8和D盘TEMP后重跑通过。 |

原始日志与截图见[证据目录](../evidence/operator-administrator-logs-01/README.md)。核心及原生夹具已接入同源冻结介质验证和PR Windows检查，不通过修改旧介质或扩大旧PASS实现。

## 关键页面

![操作员作业](../evidence/operator-administrator-logs-01/operator-work-1366.png)

![管理员参数](../evidence/operator-administrator-logs-01/administrator-parameters-1366.png)

![管理员日志](../evidence/operator-administrator-logs-01/administrator-logs-1366.png)

![紧凑日志](../evidence/operator-administrator-logs-01/administrator-logs-800.png)

## 发布身份与验证边界

### 1.10.0权限边界补修

PR86发现共享数据根的凭据账可被普通Windows用户替换，1.9.x不能作为最终推荐。现由本机System认证服务保管私有凭据/锁定/会话/授权，受保护锚点阻止服务缺失时回退；软件密码登录仍允许普通Windows用户，首次开通才要求安装管理员批准。首配与运行UI只调用应用账号契约。内置RCS真实Query=false，同时修正真实连接的模拟标识；日志文件打开与轮转使用短专用锁，扫描/导出不持有，原JSON路径和完整多行堆栈分别呈现。

本机机制专项52、参数81及配置确认通过；原生管理员/日志17项和首配93项通过，包含真实登录窗口重新授权并保留原候选。实际SCM/System/受限Windows令牌测试已接入 `scripts/verify-administrator-broker.ps1` 和必需CI。本机早期普通用户在创建服务前被真实安装权限守卫拒绝，见[原失败](../evidence/operator-administrator-logs-01/broker-boundary-local-blocked.log)；该历史拒绝不算服务通过。最终CI服务验证及后来获Windows批准的本机安装/D盘迁移分别见上节，保留各自SourceCommit与范围。

![首配重新登录后保留原候选](../evidence/operator-administrator-logs-01/startup-session-restored.png)

### 中间候选历史

1.9.1中间候选曾补修PR83的既有P1/P2：越界测量原样本与拒绝事实同事务保存，重启后同ID改值进入原身份冲突守卫；重量下限无效/负数/精度错误定位下限并保留输入。PR82的MiB标签已有，校验文案一并统一。参数专项由75增至81项PASS。首配测试的“数据根不得存在”过期断言修正为禁止业务DB/设备租约，保留管理员元数据责任；新增重启夹具完整Dispose后再取得原资源租约，不放宽运行资源守卫。

管理员专项由44增至48项PASS：增加复制ticket后篡改请求hash、期限、单次编号均拒绝，真实会话到期日志断言。账号维护在密码计算后的提交点复核完整会话hash；helper授权在服务器端账绑定独立随机凭据，不依赖载荷自述。1.9.1随后由受保护认证候选及最终1.10.1替代；以下1.9.0证据保留其中间候选身份，不能套用为最终结果。

首轮1.9.0候选源码为 `1f4c4319cd4003ab5586b9a3fb2a23844d21310f`，由1.9.1替代。其同源portable和win-x64均构建/库存验证通过，随后安装器重建共享DLL并与嵌入介质逐字节核对通过。保留原身份，不将介质重标为新SourceCommit。

| 同源交付 | SHA-256 |
|---|---|
| `CainiaoSupplyStation_SM_Setup_v1.9.0_win-x64.exe`，177944734字节 | `2cfa62f816eb268b94cbe6a896ae099d52a9bf648260c01a8bb5b03e4d196dd7` |
| win-x64 ZIP，738项文件库存 | `78e593ddef247ee27c015acc447d9494c2ffed3880a1c4a6c5d7a4f1c39d788c` |
| portable ZIP | `e61f984b7c225a01e0e1b175f2ff5ea451551d1c700dc50d192bfeccea7e06e2` |

[发布回执](../evidence/operator-administrator-logs-01/RELEASE.json)、[安装器回执](../evidence/operator-administrator-logs-01/INSTALLER.json)、[冻结原生检查](../evidence/operator-administrator-logs-01/frozen-native-administrator.json)。同一提交的固定夹具针对实际发布DLL执行：管理员/日志原生16项及首次配置90项通过；核心管理员/日志44项通过后，完整商业恢复仍在Windows安装目录资格处失败，故[完整核心结果](../evidence/operator-administrator-logs-01/frozen-core-blocked.json)明确为FAIL/BLOCKED。738项介质文件未被夹具修改。该候选原图现保存在[1.9.0原图](../evidence/operator-administrator-logs-01/intermediate-1.9.0/README.md)，不再作为上方当前截图；早先工作树结果独立保留。

实际自包含发布EXE以 `--environment-report` 启动、取得报告并正常退出0，见[启动冒烟](../evidence/operator-administrator-logs-01/published-exe-start.json)。这覆盖EXE引导和环境报告，不冒充完整安装/设备业务启动。首轮发布因本机NuGet缓存配置与依赖许可收集器默认路径不一致失败；设置真实 `NUGET_PACKAGES=D:/DevCache/nuget/packages` 后重新完整发布通过，没有放宽许可检查。

公开归档针对main和本轮规范Git字节分别运行：同为298项历史问题，新增0。修正整合PR遗留的6项清单内容身份；未改归档校验器、忽略规则或历史问题，见[比较回执](../evidence/operator-administrator-logs-01/archive-comparison.json)。

复现使用 .NET SDK10.0.402：`dotnet run --project tests/Ipc.Application.Verification -- <仓库路径> --operator-administration`；Windows原生用 `dotnet run --project tests/Ipc.Desktop.UiVerification -- <仓库路径> --operator-administration`。发布按 `scripts/release-windows.py --profile sportmaster` 与 `scripts/package-installer.py`，实际介质参数及同源提交见回执；冻结验证用 `scripts/verify-commercial-wpf.ps1 -OperatorAdministration`，完整核心用 `scripts/verify-commercial-media.ps1`。本机TEMP/TMP改用D盘以保留规定的存储余量。

完整商业恢复在本机普通账号下被Windows部署目录权限拒绝，原失败保留；不放宽权限检查或将它记为PASS。首次错误运行的C盘空间保留守卫及无工作区参数启动错误也保留，未污染交付输入。

本机Windows 125%缩放，截图是指定DIP客户区的原生渲染，部分大客户区超过物理工作区；不声称目标工控机、物理100% DPI、触控、完整IME、双Windows账号、完整安装器EXE交互/重启验证通过。获真实UAC批准的同机迁移及同源安装升级只按上节实际范围记通过。真实SDK、客户相机实体语义、真实RCS、现场安全与验收仍未验证；RCS权威结果查询当前不支持，Production缺必需能力继续拒绝。安装器未签名，未提供签名证书；旧发行及其证据保持原身份。
