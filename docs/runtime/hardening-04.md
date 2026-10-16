# WP-IPC-RUNTIME-HARDENING-04 结果

实施基准 main `752d5a89a9bf2a5e54c2551a3af77b82e1bc1961`；代码提交 `2619a7a2a485a8176ae15ea7ed6fe0a18d34d9f6`，开发分支 `codex/wp-ipc-runtime-hardening-04`。开始时无开放PR，原工作区未跟踪客户资料保持原样；本包使用独立worktree。交付为F-02参考产品工程加固，不是旧现场迁移或生产供包批准。

交付PR：[PR #3](https://github.com/yuan2go/importer-pc/pull/3)（保持开放，不自行合并）。

本轮机器结果和日志指纹见 [checks](hardening-04-checks.json)，当前恢复步骤见 [operations](operations.md#hardening-04-恢复与请求处置)，范围见 [工作包](../work-packages/WP-IPC-RUNTIME-HARDENING-04.md)。RUNTIME-03交付和历史D/RV证据保留，不倒写。

## 已实现与已验证

- 过载：源停止接纳新输入，已接纳事件继续保存处理；请求领取/发送继续受守卫约束。去掉累计missed定时重写标记；实际拒收同步保存独立标记，确认绑定当前证据版本，先审计再删除。排空、pending、运行故障及维护权限分别给出禁止原因；新过载和未确认重启都保留阻断。
- Prepared：原身份过期或发送前受阻时，只有无尝试或每次尝试都有持久NotDelivered证明且无响应，才能由维护员带依据本地终结。请求、Outbox、作业/会话和审计事务更新，原身份/报文/绑定保留。终结后进入AwaitingDisposition并保持独立阻断，不释放此前预留、不证明实体离场、不允许新实体入场。
- 发送竞争：最终守卫与过载锁串行，先持久保存TransportHandoff授权再交传输层。之后不能再证明未交付；崩溃仍Unknown。取消与发送前阻断竞争后，旧attempt不能复活终结请求。迟到/重复回执只计量一次；终结后的矛盾证据保留为Unknown。
- 报警：活动集合/计数独立查active=1；已恢复历史另分页；Ack按ID读取，不受分页限制。350条历史之后仍可定位、已读和恢复旧活动事件；同码不同对象、来源和代次不串扰。
- UI：活动/历史分区、活动计数、待决请求状态/禁止原因/下一步、本地终结确认和过载确认守卫。窗口共享应用实例，草稿仍独立。已交叉编译；下文Windows实际操作仍NOT_RUN。
- 模拟边界：Supply只消费IRequestContract及IntegrationResponse；FixturePayload、SYNTHETIC-KEEP-v1编码和响应校验归SimulationRequestContract。保留SIMULATION-v1 JSON原字段与历史合成样本的逐字节报文/哈希；未定义真实WCS接口。仅夹具项目依赖锁增加已有Adapters项目引用，无新增NuGet包。

先在未修改main运行15组基线；新增3个复现均FAIL，分别命中互锁、活动报警被分页隐藏和标记复活。修复后运行 **24组回归PASS（9组本包+15组继承）**：实际SQLite/独立接收账，覆盖过载+Prepared、排空双通道、旧确认版本拒绝、新过载与重启、原身份过期、五类请求本地终结、发送/取消竞争、重复/迟到/矛盾回执、350条历史与对象独立恢复，以及实际SQLite终结事务失败/重启。原同箱连续实体、双通道需求裁决、关闭屏障和六个进程崩溃窗口继续通过。未删断言、未把Unknown换ID重发。

构建0警告/0错误，WPF交叉编译PASS；PowerShell发布/维护脚本语法PASS。schema1 SQL及迁移校验和、历史issues/STATUS、D登记及tokens字节与main相同，无需前向数据库迁移。新增状态需新版解释，禁止旧EXE直接读产生新状态后的库；回退采用升级前兼容备份、新根和隔离核对。

## 发布与运行

`publish.ps1`在macOS PowerShell 7.6.7完成自包含win-x64包：本地 `.artifacts/HARDENING-04-2619a7a-win-x64.zip`，118942932 bytes，SHA-256 `2d75536788c14d4df0325a6b6e2f2a74237ff9c9e5d348f28af0bb81235d2c88`。812个文件的SHA256SUMS、模块哈希、三个Windows PE入口及ZIP CRC通过。release.json固定上述代码提交，SourceCodeSha256 `1e730f9a07560dc957a4b35a1f66161b40255f1b4b2266f9ac4477721aa13730`。二进制未提交Git、未上传Release；旧发布未覆盖。

同提交的本机发布核心运行冒烟7项PASS：启动、第二进程独占、一致备份/预检查/恢复隔离、元数据导出、不可写存储、模块篡改拒绝、Production无模拟业务。该脚本的Windows字段原样为BLOCKED_ON_NON_WINDOWS，实际分类为NOT_RUN，不能代替Windows测试。

```sh
./scripts/build.sh
# 全部受影响验证；仅本包可追加 --hardening
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll .
# 独立模拟接收端；桌面启动和维护命令见operations
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

## 待联调、受阻与未执行

|状态|范围|后续|
|---|---|---|
|NOT_RUN|Windows桌面/维护运行、焦点、草稿与滚动、双窗口、布局、报警和恢复操作、启动、DPI/触控/声音|WP-IPC-WINDOWS-VALIDATION-06，按operations的现有Windows脚本逐项留证|
|BLOCKED|真实SDK/WCS、预留释放、实体处置/复位、重新授权、数量/权限/换班批准|WP-IPC-INTEGRATION-BOUNDARY-05；需对应合同和批准证据|
|NotReady|Production必需能力缺失|继续拒绝业务，无模拟成功回退|
|OPEN|历史RV-01～17及旧HTML问题|本包仅关联风险，不宣布旧问题关闭或现场验收通过|

关联RV-01/02/04（恢复/实体/双通道）、RV-05/06/07（报警、请求核对）、RV-10（身份原责任）、RV-11/12/15/16（UI状态、焦点及追溯）。Windows相关只交付代码与待验证路径。没有新增设备控制、客户接口、生产数据或受限资产；仓库保持public，不强推、不自行合并。

## 后续云端交付

用户随后要求不测试、提交推送并合并现有 PR。原报告中的“保持开放/不合并/未上传 Release”描述原任务形成时状态；本次执行产物及最终模拟包交付见[云端交付清单](hardening-04-publication.md)，实时状态以 PR/Release 为准。本次未重新执行上文验证。
