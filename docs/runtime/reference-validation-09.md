> 后续复核整改：[REVIEW-FOLLOWUP-11](../work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)。PR9复核发现波次差异常显、箱码焦点交接两项P2；Product组合层仍活来源的保留分支需补动态证据。下文a080825及原状态保留形成时含义，不能当作本次已验结论。

# REFERENCE-VALIDATION-09：纠偏实施与同版参考交付

2026-10-16（Asia/Shanghai）；执行包[AUDIT-RECONCILIATION-01](../work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)。起点main `856e4de73f09a1c4f12af798c4d21e8631a6a86f`；分支`codex/wp-ipc-audit-reconciliation-01`。**最终运行源码 SourceCommit：`a08082563bc13b2f146e71c4be9136f0469af9db`**；后续仅文档、manifest和PR回执，不把文档HEAD冒称二进制来源。

结果：08工程与09合成参考运行 **PASS**；10逐页源码落实与交叉构建 **STATIC_PATH_CLOSED / BUILD_PASS**；[Windows06](windows-06.md)环境 **BLOCKED**、W-01～06实际操作 **NOT_RUN**；[真实集成05](integration-05.md) B-01～06 **BLOCKED**。没有Windows客户端截图，未作现场/生产准入，D批准及历史RV-01～17不变。

## 原始证据与实施范围

业务修改前以`fcec3fc`保存[纠偏前快照](../archive/pre-reconciliation-01/README.md)，包含原报告/检查/工作包/入口/manifest的原字节、Git blob及SHA；明确本次用户授权实施及合成/参考测试、构建、启动、发布，不授权生产WCS写入和真实设备动作。历史“不测试”和NOT_RUN保持形成时记录，目标设计和原v1.1资产不改。主工作区未跟踪客户资料未动；隔离工作树实施，未取得新的既有现场工程/SDK/合同/批准材料。

|项|实际增量|证据与准确边界|
|---|---|---|
|08A / E-06|所有旧新作业/报警历史、请求/会话详情、配置与Unknown列表在应用所有者内检查有效身份；无权Snapshot不再查询跨箱RecentOperations，当前工位/当前箱/未决恢复诊断仍可发现|8条公开读取路径对从未登录、有效操作员、登出、有效维护员、过期逐项验证；Snapshot最近历史同时验证。桌面身份代次使旧列表/详情明确失效，不能用隐藏按钮放行|
|08B / E-10|先拥有数据根并选择最终Effective/Pending，核验站点/模式，再取得实际站点/设备资源租约，之后才创建来源；首次成功seed也持久固定；失败激活保旧并受控重启回退|seed-B/Effective-A、105条后插草稿、两站点/数据根争同设备、恢复身份/模式差异、部分租约及来源创建失败、实际来源启动计数、租约释放终态均验证；Windows账户ACL仍W-05|
|08B 配置审查修正|历史Version永久唯一，拒绝ABA复用；推广唯一Effective和旧版Superseded同事务；遗留多Effective按持久ConfigActivation顺序选取，不按草稿插入顺序|SQLite回归覆盖倒置插入/激活顺序、遗留多Effective、推广审计触发器失败整体回滚、历史版本拒绝、新版本可暂存；不增加schema或改历史迁移|
|10 / PAGE-01～08|启动/恢复、显式模拟身份、共享箱、双通道独立字段/数量/拆包、关闭、历史/详情、报警/核对、设备/维护；低频返回抽屉、常驻全局故障、独立工程控制|[逐页映射](ui-baseline-alignment-10.md)。同一Runtime/业务/发送器，普通供包直接冻结owner/revision，拆包/清空/少货有具体风险确认；0少货普通关闭、未知数量拒绝。1280×680保护，恢复区域保持暂停须人工恢复|
|10 审查修正|关键控件在真实视口中的可见矩形守卫，与辅助来源/波次滚动分开；紧凑固定详情/处置入口；历史处置读取前冻结全站/作业事实，不借实时版本授权旧确认|Standards原1项、Spec原2项经独立只读复核静态闭环；WPF编译通过。实际几何/焦点/扫码/双窗/声音仍NOT_RUN，未把源码判断扩大为Windows通过|
|09 / 07有效机制|显式迁移、来源代次/健康、当前输入失效、精确来源核对、通知次序/溢出、独立停止/释放、防重与恢复|[场景/选择器](validation-scenarios-09.md)、[同版完整运行日志](evidence/reconciliation-01/final-reference.log)。源静默不因Snapshot续期，旧来源不能重新授权实体；Unknown不重发|

保留schema1/id2/id3 SQL与固定checksum、SIMULATION-v1请求/响应fixture原字节，保留原请求ID/payload/hash/目标/合同、旧读者拒绝新增迁移、未来格式/迁移/checksum拒绝、原格式备份、回执先于迁移、失败保旧、隔离恢复。没有删断言、跳过关键场景或把异常吞掉换PASS。

## 实际持续闭环

[完整合成旅程](evidence/reconciliation-01/reference-journey.json)使用真实ProductRuntime、来源队列/显式合成心跳、单后台发送器、SQLite和独立进程接收账。流程为：启动/有效模拟身份→箱校验→左右通道显式实体入场→业务拒绝后纠正读码→左侧接受2件并独立模拟离场/复位→右侧8/4/4拆包、逐件保留4件→接收端提交接受但丢回执→Unknown阻断→受控停止/重启，身份重置、原箱/请求不变→维护身份查询原请求→唯一接受账累计6件→右侧模拟离场/复位→权威关闭→新箱接受量为0、旧箱2作业/6件及关闭链仍可查→新箱关闭→再次重启，两箱历史保留。

该次工位`SIM-c235997a575d4a8c933e07c648db2194`；丢回执原请求`1c1b8ac1805e492f9d006fd146a1ba86`。独立接收账按该ID核验发送次数恰为1；原payload/hash/target/contract与单Attempt在重启/查询后保持，不换ID试发、不把业务接受当实体完成。两箱不依赖场景重置或计时假成功。Production没有执行该旅程。

## 验证、失败修复与资源终态

宿主macOS 26.5.2 / build25F84 ARM64；实际SDK10.0.402、运行时10.0.13。锁定还原与完整solution（含WPF）构建 **PASS，0警告/0错误**；最终同一源码`--reconciliation`覆盖H04、A01 Data/Composition/Runtime、07 Closure、08 Followup/旅程与原AC/SQLite/进程中断基线，全部所选范围PASS。T1前/后、T2后、远端接受后、T3故障/提交后中断及恢复、防重/原请求绑定/唯一接受账保留原断言。

失败日志公开阅读副本也交付（只替换本地工作树/SDK路径，错误、行号与结果不变；原字节仍在本机保留，原本及公开副本SHA分别记录于checks）：

- [首H04失败](evidence/reconciliation-01/first-h04-failure.log)：旧原始app夹具未确认WCS健康，且未检查OpenContext回执；改为查询实际loopback健康并断言回执。
- [旅程首轮Conflict](evidence/reconciliation-01/first-journey-conflict.log)：启动健康异步推进revision；等待来源/传输确认后读取Login版本，不重试未知业务请求。
- [A01停止失败](evidence/reconciliation-01/first-a01-stop-failure.log)：测试留下自身fail_stop触发器，Dispose正确再报原异常；保留故障/内存屏障/独立清理断言，再撤销合成故障并断言恰一次成功持久停止。
- [首WPF编译失败](evidence/reconciliation-01/first-wpf-build-failure.log)：CS4014未显式处理Dispatcher调度，已修并完整重建。

按实际选择器定向修复后，对最终源完成一次统一关联风险收敛，没有反复运行无关全量。来源/ModuleCatalog的Stop/Dispose失败、5秒超时后同任务持有/晚完成与最终释放是动态验证；**仍活来源下Product组合store/租约保留分支为静态复核**，没有冒称这层故障已动态注入或真实SDK已验证。7项资料工具测试通过；资料manifest校验是另一检查层。

## 同版可运行包

Release=`AUDIT-RECONCILIATION-01`；Storage/MinimumReader=`3/3`；Contract=`SIMULATION-v1`。共同源码输入指纹（含src/config/scripts、锁定构建文件、solution和随包运维文档）为：

`bae6bc350e576aa726b609c9eced8aa4dc37eac0aadfc4c48f5e186e4b3a1bd5`

|包|本地交付位置|ZIP SHA256|
|---|---|---|
|portable，依赖.NET10运行时|`.artifacts/reconciliation-a080825-portable.zip`|`d625224d2109cd15a3a1e1a0a2cfe7901116c912e986fd6cb300956ba10f33fb`|
|win-x64自包含，含WPF/CLI/独立夹具|`.artifacts/reconciliation-a080825-win-x64.zip`|`0eeebec5b07995af3a2fb02beaa63abfe2a436ba70e01cc35cc865a8df623849`|

两个ZIP都解压后核对CRC、穷举文件集合及SHA256SUMS，分别130/815个清单文件，逐模块与配置指纹见[包回读](evidence/reconciliation-01/package-readback.json)。均记录同一SourceCommit且WorkingTree=false。portable实际从解压字节启动，七项发布/维护冒烟PASS：启动、第二进程拒绝、备份/只读预检/恢复隔离、元数据导出、不可写存储拒绝、模块篡改拒绝、Production不调用模拟接收端且不伪造业务成功；见[解压包冒烟](evidence/reconciliation-01/unpacked-smoke.json)。win-x64为成功交叉发布及字节回读，未执行Windows EXE。

早期`a84257b` portable包保留为SUPERSEDED_TRIAL_NOT_FINAL_DELIVERY，不是本表最终包。原历史包、报告、NOT_RUN和旧失败没有覆盖。最终包保存在当前隔离工作树的.artifacts，未把大型二进制写入源码Git。

## 复现与后续

从SourceCommit检出，使用global.json的SDK并把dotnet加入PATH；在新输出/新合成数据根运行：

```sh
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --reconciliation
python3 scripts/package-reference.py --target portable --output .artifacts/reference-portable
python3 scripts/package-reference.py --target win-x64 --output .artifacts/reference-win-x64
python3 scripts/smoke.py --product <解压目录>/app --fixture <解压目录>/fixture/Ipc.Fixture.dll --output <新结果文件.json>
```

[实际运行/配置/部署维护](operations.md)包含独立夹具、CLI及Windows启动命令。发布要求运行源码、配置、脚本和随包运维文档已提交；输出必须全新，生成物SHA会按实际字节重新记录。资料工具另用带Pillow的Python执行[工具命令](../../tools/README.md)。

完整命令、环境、85份本地DB身份、原始失败/成功日志位置及其公开阅读副本与各自字节SHA见[checks JSON](reference-validation-09-checks.json)；本地原DB/备份留在该JSON登记的合成临时根，没有拿摘要替代原请求证据。Windows06继续等待实际客户端/输入/显示/声音环境，真实能力05继续逐项取得材料和授权。提交只在开发分支，面向main创建PR供审查，不合并、不强推。

## 资料校验的独立失败边界

公开总校验 **FAIL**：原v1.1 PPTX有265个“PNG扩展名实际为SVG字节”的媒体部件，原README.source与图册有2处Canva链接标记。对纠偏前main `856e4de`的独立原字节导出执行同一工具，同样FAIL267项；两次错误集合完全一致，三个源资产SHA/blob不变，本次新增/修改文件没有该工具错误。见[原main结果](evidence/reconciliation-01/archive-pristine-main.json)、[当前结果](evidence/reconciliation-01/archive-current.json)和checks中的资产身份。原件保留字节与发布范围，不删断言/不加宽泛豁免换PASS；Canva标记只是工具命中，不据此推断链接访问权限。资料工具7项单元测试PASS与此总校验FAIL分别记录，不影响已完成的应用构建/合成运行，但也不宣称全仓资料校验通过。

## 开发分支PR回执

已推送开发分支并创建[main PR #9](https://github.com/yuan2go/importer-pc/pull/9)，创建时HEAD `f2de36ab44c4b55144ef7ea49d03aee558ab5ee6`，读回OPEN / MERGEABLE / CLEAN；后续仅本回执与manifest文档提交，最终HEAD以PR读回为准。没有配置GitHub CI检查（空check列表不等于CI PASS），本次PASS来自记录的实际本地运行。未合并、未强推，main保持起点；两个包仍固定运行源码a080825及本报告SHA。
