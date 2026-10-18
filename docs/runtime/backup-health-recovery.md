# 备份路径与健康取消 · 独立复核及修复

修复基于main **08286542bbd007433d85be2cb8d129b366b007e0**，PR19已经合并，开工与发布前无其他开放PR。运行源码 **c78746a8f4417136f8244b514326a83bba58345d**；后续仅报告/manifest/证据。交付[PR #20](https://github.com/yuan2go/importer-pc/pull/20)，保持OPEN、不合并。独立工作树保护原目录未跟踪资料。原REQ/BR/CAPI/D/RV、SQL与历史证据不改。

## 复核结果和实现

|问题|独立结论与修复|
|---|---|
|PR18 P1 / 4305731150|仍存在：BackupAsync及恢复实际清单直接使用本机GetRelativePath。新增内部RecoveryFiles，只有本机枚举结果转换为`/`；备份、恢复输入和实际集合共用严格规范，保留全局ProductValidation反斜杠/越界/链接拒绝。Windows原始故障本机未执行，不以Mac通过替代。|
|PR18 P2 / 4305731157|ReferencePeer仍传播OCE，但PR19已经在阶段和巡检处捕获；原“超时必然终止阶段worker”不再是最新main完整事实。新发现启动没有预算，原捕获把停机视为失败。统一启动/阶段/巡检ProbeHealthAsync，仅自身TimeoutMs预算耗尽转不可用，生命周期取消传播；ReferencePeer有限读取健康标记，保留调用取消。|
|关联数据保护|恢复创建目标之前验证SHA256、完整文件集合、重复原始JSON键、Windows保留名/尾点尾空格、大小写和文件目录别名、越界及逐层链接；禁止递归跟随链接。新文件独占创建、Flush及复制后验hash，失败保留无完成标记的残件。备份前重验未释放图像与原作业metadata，目标不得位于运行目录；不删除或覆盖原业务数据。|
|关联取消语义|旧Supply LoopbackIntegration自身HTTP超时仍false，真正调用取消传播。PLC确认查询失败保留先前接受/拒绝及原回执；发送后取消仍Unknown/单次原尝试，重启只能原查询，不自动重发。两处均先新增FAIL回归后修复。|

方案/边界及运行操作见[工作包](../work-packages/WP-IPC-BACKUP-HEALTH-RECOVERY.md)、[随包运行指南](application-capability-19.md#备份路径与健康预算修复)。不新增框架、配置字段、公共协议、迁移或依赖。旧非规范反斜杠清单继续明确拒绝并保留原件，不静默篡改原身份或重签同版本旧包。恢复仍只进全新同Application隔离根、暂停且无登录，不回滚独立接收账。

资料校验[本轮结果](evidence/backup-health-recovery/archive-checks.json)：新增文件/hash/链接错误0；原有265项PPTX媒体类型和2项UI元数据问题保持FAIL，相关资产从基线未改。

## 实际验证

|范围|结果与证据|
|---|---|
|固定SDK10.0.402核心构建|PASS，0 warnings/0 errors；[frozen-build](evidence/backup-health-recovery/logs/bhr-frozen-build.log)|
|新增风险选择器|PASS：实际未知上传图像备份/恢复、原图hash、原请求与单次独立执行、清单安全负例、HTTP headers/body预算、启动及后台恢复、调用取消、发送前/后停止、已接受PLC原查询取消；[targeted](evidence/backup-health-recovery/logs/bhr-frozen-targeted.log)|
|受影响原回归|AC17资源/停止/原回执重放/隔离恢复，AC18的24可选组合及图像故障恢复，AC19独立入口/会话/重量/两类上传/释放及非阻塞恢复均PASS；[完整证据](evidence/backup-health-recovery/README.md)|
|真实解压发布进程|3个portable候选；三默认主链+WcsToPlc前置/附加图像两条独立HTTP接收进程旅程，共5次PASS；[候选身份](evidence/backup-health-recovery/candidates.json)、[原始结果](evidence/backup-health-recovery/checks.json)|
|负证据|main基线启动超时/停机健康事实/超大响应FAIL；关联两处取消回归修复前FAIL。原AC17未正确等待/未断言暂停的失败保留，测试同步修正后PASS，未放宽业务守卫。|
|Windows/现场|Windows实际采图备份恢复、普通账户ACL/重解析点、UI交互NOT_RUN；本次无UI变动，WPF交叉构建NOT_RUN。真实SDK/实体/协议/现场BLOCKED，Production继续NotReady，D/RV不关闭。|

复验基于固定源码的新工作树，先按[运行指南](application-capability-19.md)安装固定SDK并构建，然后顺序运行：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --backup-health-recovery
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll .
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --site-capabilities
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --capability-closure
```

发布沿原package-application.py：每个Application选择portable、全新输出，真正解压ZIP后运行smoke-application.py。两个可选旅程另构建Ipc.Fixture并带`--optional-features --peer-fixture`，附加旅程再带`--attached-upload`。精确日志及hash在证据目录。没有运行全量UI/未受影响业务测试、没有把旧候选当新代码验证。

取消来源必须由拥有预算的调用方区分；.NET的GetAsync取消和超时均可能以OperationCanceledException表示：[官方API](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.getasync?view=net-10.0)。Windows保留名与尾点/尾空格限制：[Microsoft文件命名规则](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)。这些依据不构成Windows或现场测试。

PR18两条原评审线程在发布前复查仍UNRESOLVED，本轮通过新main PR交付；不以代码修复或本机PASS自动更改历史线程/验收状态。
