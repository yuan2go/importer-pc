# DEFERRED-HEARTBEAT-01 · 独立延后与恢复配置

2026-10-26。基线为执行时最新main `3a5b1d13ec5969213db35ed43d72a5aecd437777`，其中PR107已经合并。原PR HEAD `0fa4c69c`及CI run 40188686996成功是历史事实；P2审查[4370492165](https://github.com/yuan2go/importer-pc/pull/107#discussion_r4370492165)指出的问题仍在main，新增回归先复现后修复。工作区`codex/deferred-heartbeat-validation-01`独立于PR108及用户原未提交内容。

## 实现与约束

| 路径 | 本轮行为 |
|---|---|
| `RcsConfigurationValidation.HeartbeatMapping` | 先校验全量Reader稳定身份、数量、唯一性和系统StationCode，再从同一Effective映射非延后Reader。已配置Reader缺失/重复编码继续拒绝；不采用延后草稿的旧编码。静态预检和实际RCS组件共用此规则。 |
| `SportmasterRcsTaskService.ConfigureRuntime/StartAsync/HealthAsync` | 拒绝以延后RCS上下文初始化真实组件；全部相机延后时没有心跳循环/空POST，健康不可用且说明待配置。单侧配置仅发送该侧映射，index从1连续编号；HTTP成功依旧不表示业务接受。 |
| `ParcelRuntime.MaintainDevicesAsync` | 选定延后项在维护副作用前返回Rejected；全量重连仅选择Effective中的已配置项，全延后返回Rejected。Pending不改变端口、映射、代次或业务资格。 |
| `ParcelRepository.RegisterIntegrationRequestRanges` | 延后配置尚未声明有效编号范围，不能拿未填参数构造RCS选项。跳过这些配置候选；已有编号范围、永久高水位、在库与归档原请求绑定仍全部保留并校验，原备份范围退役逻辑不变。 |

首次配置仍由完整候选的字段检查、Application结构校验及工厂静态闭包校验共同执行；Production真实资格单独检查，缺SDK/批准/策略不回退模拟。运行时延后Reader使用无socket/SDK、只报告Unknown的端口，延后RCS使用不可用端口。任何延后项仍阻断新业务。配置补齐需保存Pending并受控重启；未知结果继续绑定原报文、合同、目标、编号和配置，不自动重发，也不靠重连或新配置消除原责任。

本轮不修改配置schema、SQL、ABI、已发布扩展包或既有介质；实际构建以Git SourceCommit和文件哈希区分，不重签覆盖历史包。未决旧请求需要匹配其原精确实现，原启动兼容守卫保持。

## 定向验证与复现

Windows命令均先执行`. ./scripts/project-environment.ps1`，测试和缓存均位于D盘。

```powershell
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --deferred-connections
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --parameter-guidance-startup
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --rcs-heartbeat
dotnet build tests/Ipc.Desktop.UiVerification -m:1 -nr:false -p:RestoreLockedMode=true
dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-guidance-startup
```

七个mask的bit 1/2/4分别代表左相机、右相机、RCS延后，覆盖所有非空组合。每种组合检查编辑器、真实Production离线预检和保存、初始化、实际loopback连接/HTTP报文、无空心跳、不可用投影、定向及全量重连、Pending隔离、补齐后受控重启和左右同码独立请求。全延后恢复后另制造Unknown，验证重连及重启不重发、不改原请求、不激活破坏责任的延后候选。生产文件和客户设备未参与。

Windows本机未配置机级运行策略，真实工厂申请设备租约按原保护要求拒绝；该失败保留，不修改HKLM/ACL或安装状态。Windows运行矩阵使用测试工程内显式隔离端口装配，调用真实TCP/RCS适配器及Application；实际工厂的静态预检仍使用生产代码。Linux CI运行同一矩阵时使用真实`ParcelCapabilityFactory`，覆盖其设备会话选择和资源租约。Windows原生专项通过实际WPF控件切换七种延后组合并调用真实离线预检；已有全延后原生作业窗口与准入断言继续执行。保护安装/普通账户资格另由现有Windows CI门禁计证，不能将隔离夹具写成已安装产品验收。

原始复现、回归及原生结果见[证据清单](../evidence/deferred-heartbeat-01/README.md)。新增矩阵已加入既有CI，未移除或跳过原门禁。CI及最终提交身份在PR上按最新HEAD单独核对。

源码`2d02ea30`冻结Windows x64介质及同源发布DLL的参数/延后、首次启动原生专项均PASS，实际EXE环境报告启动ExitCode=0，固定源码及库存/二进制哈希见[冻结复验](../evidence/deferred-heartbeat-01/README.md#固定源码原生复验)。本机工作树首配22条、心跳27条、七组合矩阵130条、WPF专项88条PASS；额外参数重构测试在21条静态PASS后因缺少已安装机级运行策略中止，整项未通过。真实工厂矩阵已由实现提交对应Linux CI执行通过；最终集成以[PR109](https://github.com/yuan2go/importer-pc/pull/109)最新HEAD的完整检查为准。

真实相机/SDK、真实RCS合同/响应、目标IPC、物理触控/100%DPI、24h/72h及现场验收未执行；原D/RV继续保持原状态。
