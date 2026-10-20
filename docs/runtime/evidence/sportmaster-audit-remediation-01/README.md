# 审查整改证据

[结果与R-02分析](../../sportmaster-audit-remediation-01.md) · [任务范围](../../../work-packages/WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01.md)。原独立审查D-01～03和原型D编号分开；R-01未决不改。

## 最终同版证据

最终源码、介质和夹具均为 **934fd4af6a5b5587021892c0f9df169510115db8**。D-01补齐展开筛选挤压边界后，重新构建`.artifacts/ui-final.zip`，746项库存；同版完整视觉、首配/辅助页与R-02均PASS且FixtureMatchesMedium=true。

[介质身份](ui-final-media-identity.json) · [视觉来源/DLL](ui-final-native-result.json) · [视觉日志](ui-final-native.log) · [辅助页结果](ui-final-pages-result.json) · [辅助页日志](ui-final-pages.log) · [性能来源/DLL](performance-result.json) · [逐项样本](R02-performance.json) · [性能日志](performance.log) · [最终构建](ui-final-build.log)。这些是当前主证据，后文历史候选不重标。

最终图像：[默认](ui-final-normal-1366.png)、[classic](ui-final-classic-1366.png)、[最小筛选编辑区](ui-final-history-filters-minimum.png)、[筛选/时区](ui-final-history-filters-1100.png)、[非法日期](ui-final-history-invalid-date-1100.png)、[记录/原件](ui-final-history-1100.png)、[最小恢复动作](ui-final-history-expanded-minimum.png)、[支持拒绝](ui-final-support-invalid-date.png)、[Unknown/拒绝](ui-final-unknown-rejected-1100.png)、[失效](ui-final-stale-1100.png)。[图像元数据](ui-final-images.json)记录实际像素；classic-1366是旅程标签，不能据文件名推断客户区，classic精确客户区未单独量测。

最终同版复跑省略FixtureCommit，分别使用`-VisualProductization`、无该开关的辅助页模式、`-AuditPerformance`；三个新Output共享同一Media。下面独立夹具命令仅用于历史ff2e7a7样本。

[最终归档差额](final-archive-delta.json)检查文档封存de4b5b1与最新main ad6dc2e的完整规范Git树：双方同为267项历史错误、无新增；[封存身份核验](sealed-identity-check.json)检查全部证据散列、8份原Git blob及三个同库存结果。后继只增加这两份回执及索引/manifest，不将历史全树FAIL写成PASS。

## 历史候选与失败追溯

R-02分析介质来源`ff2e7a7459db148d0ed8dd4537489710b402635f`，独立夹具`504c187e1eeb87fa993424f11ee3406f1b06b56a`。夹具以包内DLL为引用，使用已有internal合成身份/端口接缝，客户端未增加绕过。两者生产源码/配置无差异，源码/介质/夹具三账独立。该ZIP仅在本地`.artifacts/auditfix-final.zip`，746项库存，不能据此宣布商业发布。

提交前初次整合main PR59（ad6dc2e）的候选与夹具为`892a40478d548fccd01dfb4543efeac48ff38d38`，`.artifacts/integrated-final.zip`，746项库存；两个EXE均NotSigned。同版视觉、首配/辅助页通过，但截图复核仍发现筛选展开挤压，后由934fd4a有界编辑模式关闭。[初次整合身份](integrated-media-identity.json)、[视觉来源/DLL](integrated-native-result.json)、[视觉日志](integrated-native.log)、[辅助页结果](integrated-pages-result.json)、[辅助页日志](integrated-pages.log)、[构建日志](integrated-build.log)保持原身份。[早期路径对照](performance-inheritance.json)不用于替代最终同版实测。

ff2e7a7旧性能资料为[基线样本](baseline-R02-performance.json)、[基线来源](baseline-performance-result.json)、[基线日志](baseline-performance.log)，无受控A/B条件，不从数值差额声称产品性能改善。

|证据|解决的风险与限制|
|---|---|
|[最终介质身份](ui-final-media-identity.json)、[最终原生逐DLL与来源](ui-final-native-result.json)、[性能逐DLL与来源](performance-result.json)|同一934fd4a来源、组件/模式、库存前后校验；不是安装或真实设备通过|
|[最终原生日志](ui-final-native.log)|default/classic、日期边界/时区/中文状态、筛选/记录模式固定动作、左右独立、Unknown原Query不再发、失效、焦点/选择/滚动/草稿、共享辅助窗；原生125%合成条件|
|[性能原始逐项样本](R02-performance.json)、[运行日志](performance.log)|300个自身实体终态，驻留0/50/199/101，快照/深复制/容量/历史/Dispatcher与一/双窗短时资源；无现场预算或长期资格|
|[构建](ui-build.log)、[既有回归构建](projection-build.log)|必要编译；后者可编译不代表可启动|
|[既有回归启动受阻](blocked-existing-projection.log)|缺机器政策处fail-closed；原测试/政策未放宽，不能计PASS|
|[初次性能失败](failed-invalid-id.log)、[原件原因](failed-invalid-id-reason.json)|错误的大写合成ID被InvalidAdmissionId拒绝；没有作业/外部意图。先前推断的健康时序不是已证明原因；修正夹具合法ID，未改变准入规则|
|[复合焦点断言失败](failed-focus-retention.log)|原夹具没有确认初始焦点；不能从该复合断言追溯哪个子项先失效。补激活/初始焦点断言，保留原刷新后完整断言；同一介质独立复验PASS，失败保留|
|[开发增量加载失败](failed-incremental-xaml.log)|曾发生BAML/connectionId类型不一致；清理生成物并完整重建后复验通过，未删断言或改业务换PASS。包内原生结果独立记录|
|[最终归档差额](final-archive-delta.json)、[早期差额](archive-delta.json)|带Pillow的同一Python、Git原字节导出；双方同为265 PPTX类型、2历史metadata错误，整体仍FAIL，无新增/无豁免。运行源及日志不含生成bin/obj|
|[公开副本处理](origins.json)、[文件散列](SHA256SUMS)|原日志只替换工作区/用户临时目录并统一LF，记录原始与公开hash；JSON/PNG原字节保留|

## 原生图像

以下为真实打开的WPF窗口/控件RenderTargetBitmap导出，包含原生字体、绑定、布局与实际DPI；不是HTML截图、效果图或OS桌面录像。窗体非客户区不在导出内。

* [默认工作台](normal-1366.png)、[classic兼容](classic-1366.png)
* [历史日期/时区](history-filters-1100.png)、[历史正常及原件](history-1100.png)
* [最小客户区固定动作](history-expanded-minimum.png)：只用于恢复按钮几何，不作为完整历史任务可用性或全部字段可见通过
* [Unknown/拒绝](unknown-rejected-1100.png)、[读取失效](stale-1100.png)

旧初次整合892a404导出：[默认](integrated-normal-1366.png)、[classic](integrated-classic-1366.png)、[非法日期保留及下方动作受挤压](integrated-history-invalid-date-1100.png)、[收起筛选时固定动作](integrated-history-expanded-minimum.png)、[支持拒绝非法日期](integrated-support-invalid-date.png)。旧图不重标，最终模式修复以上方ui-final图像为准。

## 复跑

以候选SHA的隔离checkout构建新输出（不要覆盖原ZIP），SDK10.0.402：

```powershell
python scripts/package-application.py --application sportmaster --target win-x64 --output .artifacts/new-audit-candidate --dotnet <dotnet10>
```

然后从本PR checkout运行固定独立夹具；Media指向已核验的候选解包目录，Output每次全新：

```powershell
./scripts/verify-commercial-wpf.ps1 -Media <candidate> -Output <new-native> -Dotnet <dotnet10> -VisualProductization -FixtureCommit 504c187e1eeb87fa993424f11ee3406f1b06b56a
./scripts/verify-commercial-wpf.ps1 -Media <same-candidate> -Output <new-performance> -Dotnet <dotnet10> -AuditPerformance -FixtureCommit 504c187e1eeb87fa993424f11ee3406f1b06b56a
```

上述独立性能夹具对应ff2e7a7分析介质。最终892a404介质的同版原生复验省略FixtureCommit，分别执行`-VisualProductization`和无该开关的辅助页模式；RESULT中的FixtureMatchesMedium应为true。不要用504c187夹具省掉PR59新增日期断言。

入口先核库存，测试前后不改变Media；结果分别写来源、FixtureMatchesMedium和引用DLL散列。没有FixtureCommit时沿介质同SHA夹具。不要把新夹具作为介质本身已包含的修复。

归档检查：在不含生成输出的完整Git导出执行`python -X utf8 tools/verify_archive.py --root <export> --scope public`；使用带Pillow的同一解释器比较baseline和本PR。Git导出指定`-c core.autocrlf=false`保留规范原字节，避免Windows换行转换造成虚假hash差额。直接检查含默认bin/obj的开发树会产生未登记输出；缺Pillow的运行也不能与完整图像检查混计。

未执行：真实Desktop EXE指定账户安装启动、UAC/ACL/双账户/重启升级恢复、100/150%及混合DPI、物理HID/触控、SDK/RCS/设备/现场、真实峰值/长历史/长时间稳定性。未运行项不等于代码缺陷，也不等于已通过。
