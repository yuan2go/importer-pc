# CONTRACT-CONVERGENCE-03

基线main `c96cb382`。继续核查02报告的不确定项；用户授权分批修改、验证、提交、创建PR并合并main。复用D盘干净工作树，新分支codex/contract-convergence-03；开工无开放PR。原工作区和历史证据不动，CI保持停用。

## 修改前候选

| 批次 | 候选及现行合同 | 计划处置和验证 |
|---|---|---|
| B1 | DeploymentExtensions双参数构造与双值Deconstruct；当前主契约已有四参数且Services/Ports可选。自有旧二进制不兼容，ExtensionContractCompatibility要求精确Platform哈希，生产配置使用完整部署记录 | 删除冗余二进制桥接，保留可选服务/端口及完整JSON；构建全部当前业务，验证扩展草稿隔离与往返 |
| B2 | Python中Supply/Inspection源码顺序断言；现行不变量是身份拒绝先于配置读取、持久化和设备构造，并且独立维护有自己的权限入口 | 用实际StartAsync的拒绝/原件保全行为替代运行源码顺序检查；维护分支检查若不能证明等价则保留，不借机扩大权限 |
| B3 | OperatorAdministration要求已授权SM保存仍因未暂停而拒绝；还把全模拟配置切换为Production并期待旧认证层异常，实际先被当前相机合同拒绝 | 删除这两条已失去当前前提的检查，不改为捕获任意异常；现行未授权/撤权拒绝及Production旧配置拒绝保留。自动保存由MvsOfflineActivation的真实SQLite/识别链检查守护 |
| B4 | 本轮Python全量暴露真实进程释放夹具以固定0.25秒延迟假定后代已运行；失败发生在建立测试前提时 | 用显式故障触发文件同步真实子进程就绪，保留取消/失败两条资源所有权行为及全部停止断言，不改生产build_process |

## 必须核定的保留项

LegacyNamedCapabilities内的重量、能力、健康与操作员契约需追踪实际生产引用；不能只按文件名删除。原请求/Unknown、原始接收责任、退役范围与当前完整恢复、共享扩展、其他业务、维护暂停屏障及按故障阶段区分的资源测试继续依据调用入口核查。无证据证明失效或等价的保留并列出定位。

计数沿用02：跟踪的src/tests/scripts指定文本代码物理行、Python test_方法、C#检查调用点分开统计；后者不是独立测试数。Windows/SDK/RCS/现场及提权维护不继承历史PASS。结果见[报告](../runtime/contract-convergence-03.md)。
