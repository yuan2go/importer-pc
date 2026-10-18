# PR24 最新head独立复审

结论：**本次限定复审范围内，Standards与Spec均未发现剩余代码阻断。R03原schema1/2去重遮蔽反例已闭合；R01～R05参考回归通过。** 这不是整包交付、Windows/真实接口或生产放行。

固定PR head **f862c2ae2ad00ab79ceff5e2013c2a5593d05234**；main **b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**。本轮差异基线3f6f762d31655ef0fe8d6562ed413beed682e2f9；代码/测试与445e1f62bf01fe21e5f13576d2f483292571b709逐字节一致，后续提交仅文档与证据。结束核验远端仍为固定head、PR OPEN、mergedAt=null、main未变。PR23/26和原工作树未修改；未评论、编辑PR或合并。只在仓库外创建快照、独立探针、合成签名样例和运行证据。

2133个快照源文件逐一匹配固定head Git blob，无漂移。先读取main的AGENTS/README/INDEX/框架与决策，再核本包、AQD07/08、19/20/22可靠性、受控扩展、运行、公开范围和历史D/RV；不把实现者提交的PASS作为本轮执行结果。

## Standards

**0项确认阻断。** AQD08要求去重前校验原始完整库存，不以零引用替代歧义。固定源码：

- [原库存校验](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelCapabilityInventory.cs#L10)：schema1/2/3统一检查64项上限、null、库存身份唯一、角色身份独占、角色/库存全部内容一致；原始roles.Concat(inventory)逐项校验已知kind及精确包/version/module/implementation，disabled和未绑定项也不跳过。库存内即使同内容重复仍拒绝；一次完整角色镜像允许。
- [配置入口](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs#L13)先执行共用校验，出错立即返回，随后才进入ValidateTopology/All；三个主流程均复用该格式入口。
- [Factory](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Assembly/ParcelCapabilityFactory.cs#L67)在Bound/DistinctBy前调用同一规则，再执行真实包资格；没有先加载提供者再证明原库存。
- [离线消费者](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelConsumers.cs#L41)先验证Effective/Pending；逐件FrozenConfiguration在第52行验证，先于第58行All。reader、原阶段、hash和接收Owner检查仍在。未知/矛盾事实抛出，不能生成空引用证明。

工程约束及Fowler启发式检查没有需要报告的新可维护性缺陷。统一应用格式规则与装配资格各自保持职责，未引入通用流程引擎或业务SQL进Platform。原审查、29项历史失败、旧包/候选身份保留；文档不宣称Windows/生产资格。

## Spec

**0项确认缺失/错误/范围漂移。** R01～R05固定源码及本轮执行结果：

|问题|固定源码|本轮结果|
|---|---|---|
|R01 原回执身份恢复|[登记与补齐](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelReplyIdentities.cs#L8)、[RecoverRaw事务](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L212)|恢复事务失败回滚；Awaiting原raw恢复持久身份；65次Query后旧ID不同内容Conflict且Submit仍一次；旧DB4/归档BLOB补索引，旧假完成与矛盾原件阻启动。PASS。|
|R02 Attached恢复|[恢复原因归属](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L142)|已完成主链重启仍保留State/Reason；Upload独立Unknown/Failure。PASS。|
|R03 原库存/消费者/卸载|上述共用规则；[实际卸载负例](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/tests/Ipc.Application.Verification/InventoryClosure.cs#L92)|schema1/2/3三配置作用域正反矩阵、独立逐字段探针、schema1/2六个真实卸载负例及合法清偿后卸载。PASS。|
|R04 混合消费者|[装配分派](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Assembly/ExtensionConsumerReaders.cs#L11)|真实Supply DB5/Parcel DB4只读分派；混合Supply Pending阻卸载；未知/链接/歧义拒绝。PASS。|
|R05 维护期间发送围栏|[维护登记/在途计数](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs#L16)、[阶段双检查](https://github.com/yuan2go/importer-pc/blob/f862c2ae2ad00ab79ceff5e2013c2a5593d05234/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L22)|共享WCS重连阻Resume/Submit/原Query；在途同服务拒维护；左设备维护不阻右位，超时未返回保有围栏，迟到返回记MaintenanceIncomplete。PASS。|

## 独立执行与真实卸载边界

所有运行均从本次仓库外固定head快照新构建，SDK10.0.402、锁定依赖、-m:1 -nr:false；源文件未改。执行原完整选择器，没有删除断言或通过修改测试放宽守卫。

1. `dotnet build tests/Ipc.Application.Verification/Ipc.Application.Verification.csproj --artifacts-path <ARTIFACT>/build -p:RestoreLockedMode=true -m:1 -nr:false`：退出0，零警告/错误。
2. `dotnet <BUILD>/Ipc.Application.Verification.dll <SOURCE> --inventory-closure`：退出0，完整完成标记。schema1/2/3 × Effective/Pending/Frozen，每格10非法/3合法；DB/config原字节不变。三个schema的实际DirectPlc/CAS入口也通过。
3. `--review-closure`：退出0，R01～R05完整完成标记。
4. 实际Sportmaster Assembly Debug发布、精确checker验包：退出0。为独立构建新不可变测试样例1.6.11，插件源最后相关提交b82b8d07a39fe8d2a875a05aec0cebfa4aeb94ab，目标宿主为本次固定head构建。包SHA256 **628da64283ae5d56eaa92cd65fd79830a58dd39daf8ab9e4fc9adee79b8c0602**，未覆盖旧1.6.10或其他包。
5. 使用独立测试目录及精确checker公共DLL（覆盖前后hash记录signed-tests-overlay.json），执行`--quality-extensions <1.6.11.ipcx> <TestOnly trust>`：退出0。真实安装中断/沿原操作恢复、绑定/激活、主链、资源/备份/混合Supply阻卸载、清偿后卸载均通过。schema1/2×三作用域共六个隔离真实Application卸载拒绝，包保持Installed、原DB/config逐字节不变；测试没有删消费者或清理责任换Uninstalled。
6. 仓库外另写独立探针Program.cs，按9个完整绑定字段逐项制造角色镜像矛盾，并检查独立克隆合法镜像、重复精确包、null项、65项库存。在3 schema×3作用域共117个案例均符合预期，原DB/config hash逐次保留。

具体命令身份、日志hash、样例identity及边界见review-result.json。原运行根已复制到runtime-evidence/；六个坏消费者/已安装包及全部原配置/数据库保留。未复制私钥或运行trust到资料/仓库。

Frozen格式探针使用hash正确、从未发送的Prepared未结件；没有穷举每种schema×Unknown/Conflict/图像/回执状态。其他不明与原件责任沿原回归覆盖，共用格式检查与状态无关，但不将其表述为完整状态组合现场验收。

## 尚未验证及交付门禁

- Windows实机安装/运行、WPF交互/多窗、UAC/ACL：NOT_RUN。macOS真实Application与签名包卸载不能证明Windows提权/文件锁语义。
- 真实SDK、设备、WCS/PLC专用合同、实体关联、复位/联锁、现场：BLOCKED。软件暂停不是急停，Production不据参考PASS自动Ready。
- 真实断电、生产长期运行/容量寿命：NOT_RUN。本轮不重复全量测试、完整产品打包、旧Supply/Inspection完整验收或延期DirectPlc UI。
- 本轮未重新执行所有配置激活断进程点、容量长跑及历史资料267项错误扫描；它们的旧证据保持原身份，不由此复审扩成新PASS。新产品候选仍须从最终获准源码重新构建、核包/进程运行，旧7850fcc候选不获得新head资格。
- 原AUDIT01 PDF仍未取得，BLOCKED_NOT_OBTAINED；JSON/Markdown原报告和AUDIT02不能替代。历史D/RV不关闭。

Standards：0项确认阻断；Spec：0项确认阻断；各轴均无已证实的最严重剩余代码问题。
