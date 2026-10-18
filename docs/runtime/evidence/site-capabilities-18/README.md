# 18执行证据

运行源08595e9243ccd9981fc419b8074f67c836644248；基线d3a757d，设计63e04b1。[完整结果](../../site-capabilities-18-results.md)、[能力/配置/重跑方法](../../site-capabilities-18.md)。所有数据为本任务新建的合成数据，无SDK、凭据或生产图像。

- [source-freeze.json](source-freeze.json)：实际运行输入SHA、SDK/平台和测试源码hash。
- [optional-matrix.json](optional-matrix.json)：24组合实际作业/原请求payload及图像hash。
- [independent-http-receivers.json](independent-http-receivers.json)：三个独立HTTP服务接收账投影、原payload/回执、Executions=1及实际PNG字节hash。
- [candidate-inventory.json](candidate-inventory.json)：三个冻结ZIP/大小/hash与公共契约字节一致性。
- 三个Application各有`*-default.json`及`*-options-http.json`，分别证明解压后的默认链、全部可选功能/独立服务/丢回执/进程重启/原查询/显式继续。
- [wrong-composition-rejection.json](wrong-composition-rejection.json)：三选择错配配置在数据库/端口创建前明确拒绝。
- [log-custody.json](log-custody.json)：31个原始日志hash与仅替换绝对工作树/SDK路径的公开副本hash。日志包含基线、最终scope、三个WPF、依赖、打包、进程旅程及首次失败。
- [最终图像恢复](logs/image-recovery-final.log)、[原应用风险](logs/application-serial.log)、[旧基线](logs/legacy-baseline-final.log)、[旧架构](logs/legacy-architecture-final.log)、[15项发布工具](logs/packaging-tests.log)。
- [首次资料校验](archive-first.json)保留Pillow缺失与清单校验文件名错误；最终资料状态在结果报告中单列，不用业务PASS覆盖旧源资产FAIL。

这些结果不证明Windows客户端或真实SDK/接口/现场已通过。备份与独立接收数据库留在隔离合成根，公开证据使用可审阅投影及字节指纹；没有上传真实业务数据或模拟签名私钥。
