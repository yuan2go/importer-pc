# 质量闭环01 · 可复核证据

执行main 2126ac2c06a0c352d96afbbf13ec1a719c352738；最终业务/UI/候选源码3d6960756bfd843a1cb78c6648687b45f2da9f1c。checks.json固定最终各选择器、实际返回码与日志hash；test-host-delivery.json和runtime-source-files-final.json绑定实际宿主及源码。签名测试为独立宿主，公共合同/核心DLL取自冻结Debug checker，身份见signed-test-host-delivery.json；编译测试原宿主不覆盖。

checks-before-final.original.json、frozen-quality-run.json及runtime-source-files.json保留cad/e1时结果；checks-ee59989.original.json保留中间轮次。candidate-readback-e1.original.json和candidate-readback-c666.original.json不更改原产物身份；candidate-readback-ee59989.original.json保持前一轮身份；candidate-readback.json为最终3d69607两包，各独立解压核对97项。1.6.0/1.6.1、1.6.2/1.6.3、1.6.4/1.6.5、1.6.6/1.6.7分别对应四个形成阶段的Debug/Release精确包，不能互换或覆盖；只提交.identity.json，不上传私钥/信任运行根/二进制。

closed-smoke-*.json保留ee59989；delivery-smoke-*.json来自最终独立解包实际进程；WcsToPlc两旅程使用独立HTTP接收进程，接收器源码e1及当前hash见checks.fixture。delivery-cross-release.json证明同DB4跨实现仍拒接未结，旧版原请求清偿后新版才能暂停启动。delivery-cli-management.json为最终Release1.6.7新隔离store的安装/恢复/卸载/操作读回；不代替未知旧消费者盘点。

migration-custody保留原SQL1～3字节核对；其旧reader_behavior文字只描述cad时边界，最终DB1/2/3均拒绝未结迁移，最终源码和定向DB3断言为当前证据。ui-baseline-custody固定原UI来源，本轮Windows截图为0。

原PDF未取得，原Markdown/JSON登记于reviews，PDF归档BLOCKED。Windows实际NOT_RUN、SDK/协议/现场BLOCKED。资料校验原267项错误仍FAIL、无新增；archive工具business_tests=NOT_RUN只指该工具，不覆盖实际业务定向运行。历史失败、错误探针和修复后结果保留且不相互覆盖。最终AQ包含真实子进程的三个启动终止点；最终签名旅程包含仅Pending含包备份在live清偿后独立阻卸载。并行PR25只读复核记录另存，AUDIT-02不替代AUDIT-01原PDF。
