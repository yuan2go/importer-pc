# Application能力闭环19 · 实施结果

本包完成独立Application编排、公共能力、两类上传、适配边界及客户端代码贯通，并完成参考资格验证。**Windows实机NOT_RUN，真实SDK/协议/现场BLOCKED，Production继续NotReady；D/RV未关闭。**

开工main：`08b8067aa6cba6a148252296c7a5b18a27b3be65`（PR18已合并，开工与交付前均无其他开放PR）。文档前置提交：`4644a4c`。最终运行/六候选源码：**`c213aad54f5d48687efc0b56428991b6bf4f3537`**。开发分支`codex/application-capability-closure-19`；后续仅报告、清单及证据提交，不改变冻结源码或候选字节。

## 实际完成与验证

|验收|实现及实际结果|证据|
|---|---|---|
|AC19-A|PASS（参考）：三个Flow各自拥有步骤、前置、结果解释、核对顺序及完成条件；移除RequiresWcs/RequiresPlc总分派。共用生命周期、队列、事务和原请求基础设施|三个Application.cs、[现行设计](../design/application-capability-19.md)、core19/core17|
|AC19-B|PASS（参考）：Platform会话/认证/操作人公共协议；失效拒绝新扫码；接纳身份不可变；维护独立认证、暂停不授予权限。旧公共全名及构造保留，业务DTO留业务侧|core19会话/原身份/维护验证；Supply/Inspection受影响回归|
|AC19-C|PASS（参考）：启用秤必须实测，单位/精度/稳定性/来源/实体/新鲜度守卫；等待期限独立；超时/迟到/重复/下一件/重启有明确处置。测量去重与作业关联同事务，在插入测量后故障注入证明回滚无孤立去重行|core19/core18；原SQL1和Supply/Storage源字节比较|
|AC19-D|PASS（参考）：扫码采集身份先取得原图，等待重量不重拍；BeforeMain阻断依赖主链，Attached独立状态/恢复；声明需要重量的上传延后T1。总字节/积压守卫、先持久释放再删图、崩溃清理、已释放历史重启|core19、[六独立接收账/原PNG字节](evidence/application-capability-19/independent-receivers.json)|
|AC19-E|实现及参考PASS：沿原签名Store/精确加载/租约，注册login/camera/upload及原weight/WCS/PLC；实例安装/启用/绑定/健康分开。WPF结构化参数/实例/策略/维护认证/恢复/安装帮助程序贯通，禁用保留实例参数；无需JSON编辑框|Debug签名7模块激活、[实际发布程序签名旅程](evidence/application-capability-19/signed-release-journey.json)；三个WPF随Windows候选交叉构建。Windows实际UI/UAC/ACL NOT_RUN|
|AC19-F|PASS（参考）：原请求/尝试/回执责任、WCS接受与原PLC意图同T3保持；继续仅从未尝试意图，Unknown查无记录不重发。外呼释放锁、快照不等网络、暂停及时返回；停止超时保留所有权；确认事实受组件声明约束|core19/core17、baseline/architecture；原ContinuePrepared关键断言保留|
|AC19-V|PASS（参考）：三个portable ZIP真正解压，默认/前置未知恢复/附加独立恢复共9次进程旅程；三个win-x64候选交叉发布/解压/hash校验，实际Windows执行NOT_RUN|[六ZIP完整身份](evidence/application-capability-19/candidate-inventory.json)、[全部日志/边界](evidence/application-capability-19/README.md)|

签名验证分别针对Debug宿主和实际Release候选的精确DLL。发布程序实际安装/登录/采图/实测/上传/严格WCS/PLC/原查询，接收账每原请求Executions=1。旧宿主指纹签名包被新宿主拒绝的负证据保留；没有放宽公共契约或用同版本覆盖已安装身份。Supply源码/SQL1～5与场地原SQL1/checksum保留，Inspection原持久采样/关闭/重启及Supply原单实例回归通过。

## 复现

固定SDK10.0.402；`dotnet`指向该SDK。精确复现先在新目录读取冻结源，避免后续报告提交改变程序集的SourceRevisionId：

```sh
git worktree add --detach <新验证目录> c213aad54f5d48687efc0b56428991b6bf4f3537
cd <新验证目录>
```

按顺序执行，避免不同组合共享bin/obj并发写：

```sh
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --capability-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --site-capabilities
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll .
dotnet build src/Ipc.Fixture/Ipc.Fixture.csproj -m:1 -nr:false
dotnet build tests/Ipc.Verification -m:1 -nr:false
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --baseline
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --architecture
python3 -m unittest discover -s scripts -p 'test_*.py' -v
```

三个构建选择、签包、完整配置、客户端操作、冻结旧件、生效/恢复和打包/解压命令见[运行指南](application-capability-19.md)。三个portable及三个win-x64均使用该指南的package-application.py，完整命令和输出见c213构建日志。实际发布签名旅程可运行：

```sh
python3 docs/runtime/evidence/application-capability-19/probes/signed-release.py --application-root <冻结wcs-to-plc候选>/app --package <针对该候选精确契约签名的1.2.0.ipcx> --trust <独立Simulation信任> --config-template config/optional/wcs-to-plc/business.json --dotnet <固定dotnet> --output <新证据.json>
```

本地最终候选保留于开发工作树`.artifacts/capability19-c213aad/`。PR提供源码、配置、复现方法及公开合成证据；没有把候选二进制发布为已验收Release。早期候选/失败记录保留并明确退出现行身份。

## 剩余接入与资格

|项目|状态及需要的输入|
|---|---|
|Windows实际客户端、交互/截图/多窗/DPI/HID/触控/焦点、UAC/ACL/部署账户|NOT_RUN；需要获授权Windows环境，不能用交叉构建或HTML同源PNG代替|
|相机/读码及原采集关联、秤/校准/同实体时间源|BLOCKED；需要获授权SDK、设备/触发/帧身份及测量协议和实体证明|
|真实认证/退出/换班/维护授权|BLOCKED；需要版本化认证合同、会话失效及实际权限事实；不使用合成maintenance角色代替客户批准|
|WCS、PLC及上传提供者|BLOCKED；需要字段/单位/编码/认证、状态/地址/握手、格口规则、幂等/原查询与物理确认可证明范围、控制授权和安全联锁|
|现场节拍、缓存预算和异常SOP|BLOCKED；现有限额是显式参考配置，须按现场批准容量及处置规则验收；软件暂停不是急停|
|资料校验|原资产FAIL（265个历史PPTX媒体类型、2个原UI元数据标记）；保持原件与失败，新增文件/hash/链接错误0，实际[Pillow校验结果](evidence/application-capability-19/archive-validation.json)保留；不放宽校验器|

原REQ/BR/CAPI/D/RV、18以前报告、原SHA、发布包及受限资料边界未倒写。当前工程结果不证明旧现场迁移、真实取图/设备、客户批准或现场验收。
