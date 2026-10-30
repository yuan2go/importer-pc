# PR155 整合 08 证据

SourceCommit：`fba708de80438dd5d264b01d111395989de50bff`。软件检查来自同一完整整合树；本证据提交仅增加报告、索引、原件和证据，不改生产代码。来源分支完整头和 61 份生产 Blob 证明见 `integrity.json`；原始日志及三张图片均为本次合成验证，哈希见 `evidence-sha256.json`。

`ui-native-accepted.log` 是 WPF 实际落盘的 19 条断言和原请求责任结果；UI 控制台日志为空是因为验证程序写入自己的 WINDOWS-WPF.log，不能用空控制台代替断言结果。重跑退出码见 `ui-retry.exit`。首次调用宿主内存不足的原失败文本和空控制台保留，不计入通过，不伪称已经定位其根因。完整边界见 [08 报告](../../pr155-integration-08.md)。

重现：先执行项目环境脚本和报告中的构建及定向参数。`verify_integrity.py` 从当前 Git HEAD 核对生产 Blob/补档/07 原证据，并在 `.artifacts/pr155-integration-08/` 保存结果；其独立检出目录须为新目录。未运行真实 SCM 矩阵、客户 RCS、双相机或现场验收；不会以本记录关闭历史 D/RV 或旧备份基线失败。
