# main cfe6c0e 实际发行与定向核验

[实际发行](https://github.com/yuan2go/importer-pc/releases/tag/unified-main-reference-cfe6c0e)包含 8 个核心 ZIP、1 个附加 Simulation 扩展组合 ZIP 和 5 项身份/校验资料，共 14 项资产。SourceCommit 固定 `cfe6c0e0d0570986ea8c63f619e328d3891b82ef`；[服务器回读](remote-publication.json)记录全部 14 项大小与 SHA256 匹配，tag 指向该提交。旧发布和中断候选没有覆盖。

- [RELEASE.json](RELEASE.json)、[构建记录](build-record.json)、[整套校验](release-SHA256SUMS)、`builds/` 和 `dependencies/` 是实际生成的原字节资料。`custody.json` 保管所收录执行字节，目录 -text 防止换行转换；项目 manifest 另记录本目录全部文件。
- [checks.json](checks.json)限定结果范围。[整套独立复核](verify-release.log)、9 项发行门禁、2 项指南与更新后的 4 项入口顺序检查均通过；原过期定位失败与初次扩展测试调用路径错误分别保留，不改成 PASS。
- [冻结 DLL 夹具](frozen-probe.cs.txt)及[工程](frozen-probe.csproj.txt)使用实际媒介 DLL，执行记录在[完整日志](complete-frozen-probe.log)。验证离线无 DB 修复、CAS/占用、原 Effective 和种子保真、无端口构造、Production 拒绝、三种实际 Windows 媒体及附加组合的完整性/复制/注入/篡改/junction 拒绝；三种业务 Runtime API 使用真实默认身份门禁，在不存在的配置/数据之前拒绝。复现时将 HintPath 指向同 SHA 的冻结 app 目录，所有 scratch 根必须是新的隔离目录。
- [8 条入口记录](readonly-entry-results.json)对 Parcel 使用明确的只读 runtime-check，对 Supply/Inspection 使用普通运行入口。它们都因缺少机级政策拒绝，未创建业务根。[探针](readonly-entry-probe.py.txt)只在自身进程关闭错误对话框并禁用转储，不改变业务门禁或系统政策。
- 普通 Parcel 组合入口会先执行首次运行准备，而非直接拒绝。最初按“立即拒绝”假设启动的尝试在等待首次授权时超时/中断，原日志保留；不能把它计成运行门禁通过，也未完成 UAC 安装或创建机级政策。后续使用上述只读入口和直接 Runtime API 分别核查两条边界。
- [原生入口架构](native-apphost-identities.json)核对 6 个 AMD64 apphost；[重复发布](selected-media-and-repeat.json)核对独立发布的 app/desktop 共 719 文件字节一致。整包组合不同，ZIP hash 不应相同；没有声称八套 ZIP 均重复构建过。
- [扩展检查](extension-checks.json)证明当前合同接受新签名参考包，拒绝篡改、Production 和旧宿主包。独立 `synthetic-trust.TESTONLY.json` 仅用于复现，是公开合成测试策略，不是生产信任；没有随媒体自动复制，也没有安装或激活。私钥从未纳入本目录或发行。

Windows 完整 UAC/ACL/第二账户/受保护安装/中断升级/实际桌面交互，已有 DB 的提升维护与真实 SDK/协议/设备/现场均未取得通过证据。完整边界见[交付记录](../../unified-release-main-20261020.md)。较早 19 项扩展测试的 18 PASS/1 WinError1314 保留在标明 0f2cd71 来源的日志中，不冒充本次全部重跑。
