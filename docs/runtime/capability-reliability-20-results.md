# 可靠性闭环20 · 实施回执

**已完成本轮通用实现、参考可靠性验证和候选检查，未声明整体生产就绪。** Windows实际执行NOT_RUN；真实SDK、认证、采集关联、秤校准、WCS/PLC/上传协议及现场权限/SOP按提供者分别BLOCKED。D/RV不变。

执行基线main **08286542bbd007433d85be2cb8d129b366b007e0**（PR19已合并）。文档前置d2f5f23；核心实现43bbdab；最终运行、签名包及四候选源码 **a147eac7e4bafb70e16b2c677c2a2933909234d3**。97d9efc候选属于升级守卫补齐前诊断，已退出现行身份，原字节/hash保留。本包开发分支codex/capability-reliability-closure-20，不自动合并；并行PR20备份/健康恢复未合并进本包。

## 逐项结果与实际入口

|范围|完成情况与证据|待确认/限制|
|---|---|---|
|A 能力组合|PASS_REFERENCE：ParcelRuntime/Guards/Images/Stages从原重量策略和独立StageFact判断责任。三个实际Application入口验证Attached采图Unknown仍阻断下一件且重量到期，原采集查询不复活期限；Rejected在Disabled/Measured下均终结，迟到重量不建WCS/PLC。重量关联与去重仍同事务|采图失败现场SOP须协议批准，参考策略为终结拒绝或原采集查询，不能重拍当前帧|
|B 临时图像|PASS_REFERENCE：无图Prepared/Attempts=0安全取消持久ImageReleased，重启仍释放额度；Unknown/Conflict/在途/原回执缺口保留。释放先持久后删，19原崩溃清理和未知原件保留断言通过。满额度只限制新准入，不锁死现有件Resume|限额与节拍须现场核定，未扩大上限或清空缓存|
|C 客户端/部署|PASS_LOGIC_AND_CROSS_BUILD：测试链接实际ParcelViewModel/CapabilityEditing源码（只替换Visibility枚举），慢原查询期间Pause/Refresh及时返回且普通写互斥；Target只读并在保存重验。维护保存读回Pending、Effective及原件不变。候选包含三个实际PowerShell依赖和选中WPF闭包|Windows多窗/交互/DPI/HID/UAC/ACL/指定账户实际NOT_RUN；受控设备身份迁移按用户允许保持独立部署边界|
|D 组件/资格|PASS_MECHANISMS_AND_SYNTHETIC：实际签名1.3.1安装、精确加载、schema/能力、登录、采图、重量、上传、WCS/PLC及原查询通过；独立合成签名schema探针接受自有HTTPS endpoint，未知字段仍拒绝。资格验签/精确配置及契约hash/到期/实际前置文件、测试根隔离均验证；真实身份按签名声明校验，Production不再无条件拒绝所有真实托管组件，也无模拟回退|仓库只有参考提供者，无新获授权真实协议/SDK，具体真实适配BLOCKED。P/Invoke/任意原生加载仍需单独批准边界。资格的正例是验签器合成测试，不是生产批准|
|E 有效性/交付|PASS_REFERENCE：schema2显式UntilDispatch/AtAssociation；schema1原件保持持续新鲜。WCS等待/暂停后过期可检查处置未发PLC，保留原WCS接受/格口/报文/测量；参考关联时新鲜可继续同一重量。拒绝缺业务接受/物理确认/完成查询路径的PLC配置，接受不冒充物理完成。旧schema1未结原件在迁移2之前拒绝升级，保留旧宿主清偿入口|AtAssociation、采图失败及PLC物理完成语义须具体现场协议批准并绑定独立资格，未替现场选择默认|

Sportmaster交付为WCS接受和有效格口；DirectPlc/WcsToPlc成功需权威物理完成，拒绝/本地处置为终结失败。原作业、采集、测量、请求、尝试、回执与操作人、配置、目标、实现hash保持；WCS接受/格口/PLC原意图同事务，Unknown查无记录不当作未执行。停止超时、回执原件及原配置恢复断言仍通过。

## 验证与候选

复现冻结二进制先使用独立checkout：`git worktree add --detach <新目录> a147eac7e4bafb70e16b2c677c2a2933909234d3`，进入该目录再构建，避免后续报告提交改变SourceRevisionId。

固定.NET10.0.402，实际命令、日志hash及结果见[checks](evidence/capability-reliability-20/checks.json)。核心选择器：

```sh
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --reliability-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --capability-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll .
dotnet build tests/Ipc.Verification -m:1 -nr:false
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --baseline
python3 -m unittest discover -s scripts -p 'test_extension_build.py' -v
python3 -m unittest discover -s scripts -p 'test_release_documents.py' -v
```

AC20有44条直接断言，含真实ViewModel入口、原件取消/重启、旧读者迁移门槛和资格负例；复用19/17的原身份、防重、恢复、停止及事务断言。未重跑18完整能力组合或全部六Windows/portable候选。没有为覆盖率新增全矩阵。

[四个最终ZIP身份](evidence/capability-reliability-20/candidate-inventory.json)：三个portable与一个WCS→PLC win-x64，均从a147eac新目录发布、真正解压并核对SHA256SUMS及app/desktop的application-release.json；三个所属WPF工程交叉构建通过（仅WCS→PLC打Windows候选）；Windows包核实protect-release.ps1/provision-runtime.ps1/provision-extensions.ps1与源码一致。三个portable默认链及一个WCS→PLC Attached/认证/实测/未知上传/重启原查询进程旅程通过，独立接收账每请求一次。Windows候选只交叉构建和解包，未执行。

本地候选与解包位于隔离工作树`.artifacts/reliability20-a147eac/`；不覆盖19发布，也未新建GitHub Release或上传二进制。新能力包1.3.1、观察/集成参考版本1.0.8；Application.Contracts=1.3.0。旧精确包、SQL1和Supply SQL1～5/checksum保持，[原边界核对](evidence/capability-reliability-20/original-boundaries.json)。Parcel增加迁移2，合法旧库先清偿/一致备份，禁止回滚新库给旧程序读。

## 失败、环境与剩余事项

基线红灯明确复现：三个主流程Attached采图Unknown漏掉准入阻断、Rejected后实测复活，以及无图取消后Abandoned/Cancelled但ImageReleased=false、额度不释放。原日志保留。原17首次备份失败来自测试在第二件仅建档时发Pause的CAS竞争，夹具现等待原阶段完成，备份/恢复断言未删。无图取消夹具同样等待实际可处置守卫而非仅看到Prepared；首次组件endpoint测试被原schema正确拒绝，改用独立合成签名schema探针，未放宽未知字段校验。

资料检查最初系统Python缺Pillow；改用已安装的完整Python环境核验，不修改原图或降低校验器。历史资产/元数据问题保持，完整原始检查及新增文件身份结果见[证据入口](evidence/capability-reliability-20/README.md)。这些问题不重记成业务PASS。

需现场提供：真实认证/维护权限与有效期合同；相机原帧/触发关联、秤测量/校准/时钟与实体证据；WCS字段/单位/编码/鉴权/接受及查询；PLC地址/握手/控制权/物理完成；上传认证及原件责任；缓存/节拍/异常SOP和Windows安装账户/UAC/ACL验收。缺某项只阻塞对应生产能力，通用接入和本轮已知修复已交付。原REQ/BR/CAPI/D/RV、历史FAIL/UNKNOWN、发布包身份及公开范围保持。
