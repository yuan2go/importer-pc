# 集成验证证据与复现

本目录为2026-10-24本轮新增合成/隔离证据。全部工程、介质、缓存、测试及数据位于D盘；未安装或发布正式介质、未调用现场设备/真实RCS，未修改机级权限或生产数据。完整本地工作目录 `D:/projects/ipc-integration-evidence-20261024`；私密工作区盘点和安装位置只留本地。

## 身份与首次失败

- `d716697`：首次整合树。`local-tests.json`分别保留11个选择器结果；双TCP在失败启动后SQLite关闭抛空引用，原`dual-camera-communication.log`不覆盖。其他PASS仅对应该树。
- `race-before-fix.log`：在`d716697`基础上仅加入边界观察及延迟的未提交探针，保留原放行顺序，明确反证“失败审计先于逐笔去向”。该工作树探针不伪称原提交二进制。已提交可重现夹具在`ae012a0`；恢复该提交父版的旧失败放行顺序、保留三个Boundary点即可复现时序失败。`race-fixed-dual-camera.log`覆盖失败/取消两条路径。
- `464e8e2-tests.json`：加入启动串行和保留优化后的三项核心专项PASS。初次SDK命令缺精确`IpcContractPath`被正确拒绝，原日志单列；指定同源冻结Platform后35项托管探针PASS，未取得厂商DLL/双机资格。
- `retention-plan/`：使用实际SQL1–9、从原PR99和修复源码提取的SQL、相同冻结SQLite3.54.0。10万审计条目、100次检查由约992ms降至约10ms，另有一次准备约10ms；计划从`SCAN a`变为TEMP主键查找，同时验证另一个owner的Pending命令仍阻断。内存合成查询的配对结果仅排除逐件扫描缺陷，不是全原件规模、8GB或维护RTO资格。`retention-final.log`另验证每次维护重建集合及原有未决/永久防重保护。
- `native-TaskHierarchy`、`native-ManagementMode`、`native-Win11Stability`：`464e8e2`同源冻结DLL/同源夹具的原生结果。TaskHierarchy原断言PASS并不足以覆盖暂停回执和原责任同时占高时的完整历史行；后续严格反证和修复另计，不能借旧PASS关闭新场景。
- `native-query-before-fix`：旧冻结DLL `464e8e2`、严格夹具 `04303bb`，明确`FixtureMatchesMedium=false`。在800×420、实际125%下完整结果行断言FAIL，截图与原日志保留。

最终同源原生结果与Release身份在本目录最终RESULT及对应独立子目录登记；未执行的Windows11其他DPI/版本、真实SDK/RCS、目标4核8GB、完整raw规模/受保护维护全链、24h/72h保持NOT_RUN/BLOCKED。旧审查PDF/ZIP、旧发行介质及D/RV不改写。

`b2b2f0f`的`final-native-*`结果保留第一版高度预算的严格几何FAIL、管理及输入专项PASS；不改标为最终通过。最终冻结为`cbb04528e48d39f9d89bb39fc6d7b625ae73f171`，ZIP哈希见`release-query-fixed/SHA256SUMS`；最终三组使用`accepted-native-*`，同源DLL与夹具`FixtureMatchesMedium=true`。`464e8e2`结果、旧DLL新夹具反证、分数DPI失败均保持原身份。

上述cbb0452是本机UI续修的冻结阶段。PR100首轮CI全绿后、合并前发现健康空闲时日志保留未按定时器推进；`journal-followup/`保留新增测试在旧实现超时的反证，以及恢复周期维护后的四项专项。续修源码为43e03ce，本机程序集实际从d4e42c3加同字节续修的工作树编译，文件hash单列，不冒称新冻结发行。最终PR新合成源码的Linux/Windows流水线需重新验证；原40095971023及本机cbb0452记录均不倒写。

## 运行方式

所有命令先从对应源码工作树执行 `. ./scripts/project-environment.ps1`。

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --win11-stability
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --dual-camera-communication
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --architecture-quality-remediation
python scripts/release-windows.py --profile sportmaster --output D:/integration/新固定候选
# Media为上述输出中的sportmaster-win-x64-<源码短SHA>目录。每次Output必须新建。
./scripts/verify-commercial-wpf.ps1 -Media D:/integration/固定候选/sportmaster-win-x64-实际源码 -Output D:/integration/新层级证据 -TaskHierarchy
./scripts/verify-commercial-wpf.ps1 -Media D:/integration/固定候选/sportmaster-win-x64-实际源码 -Output D:/integration/新管理证据 -ManagementMode
./scripts/verify-commercial-wpf.ps1 -Media D:/integration/固定候选/sportmaster-win-x64-实际源码 -Output D:/integration/新稳定性证据 -Win11Stability
```

复现SQL计划可运行本目录`retention-plan.py`，先按其顶部三个路径指定源码、独立证据目录和匹配冻结介质；它生成只读源码派生的SQL及独立小型C#夹具，不修改产品代码。历史编译探针原失败保留在本地；最终运行源码及结果在`retention-plan/`。`FILES.json`登记本目录其余文件原字节SHA256并排除自身，不包含安装包、SDK或真实业务库。
