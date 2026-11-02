# PR175整合后的读码反馈验证

最终产品/测试/脚本源 `ebb966866e25fdea2cc0d370c070edaa4fea470c`，完整整合 main/PR175 `9ccb91abb37bb93df9a89bcee6956820eee8a285`。与整合前da26848a的516项证据分开，不改写旧SourceCommit。原始失败、负向复现及功能闭环仍见[本包原证据](../sm-reader-feedback-22/README.md)。

同一整合源实际运行三个Release构建0警告0错误，19套件569项PASS（10套应用349、9套原生WPF220）。`summary.json`为本次实际命令/退出码/计数，分段汇总及每套原始日志均保留。DB11兼容升级53项覆盖严格原结构/SQLite名称校验、完整备份、原回包/原请求字节、WAL、回滚、部署重试、未发送意图关闭的限制及升级后暂停/明确Resume；没有针对实际本机原库执行升级、关闭或安装。

原生PC21夹具沿PR175的ClickReady，并保留实际选中诊断页且刷新动作已启用的完成判定。新增实际Busy禁用/完成恢复断言继续通过；所有输入焦点、PDA、呈现顺序、四字段/字号、码制与音频断言保持。入口信任拒收、单调反馈超时/epoch、不复活旧格口、跨位背压及原请求守卫均在整合源重跑。

`publish-smoke.json`、`assembly-hashes.json`、`publish.log`绑定整合源framework-dependent no-build发布；实际EXE `--environment-report`退出0，版本 `1.0.0+ebb966866e25fdea2cc0d370c070edaa4fea470c`，三段WAV发布闭包完整。三个截图来自本次实际原生套件，合成设备/条码；不是现场照片。`hashes.json`覆盖目录除自身外全部证据，Git禁止换行转换，核对的是实际Git blob字节。

复验脚本 `scripts/verify-reader-feedback-22.ps1`：干净D盘工作树先Core再Native（需要时可在三个构建完成后使用独立报告并行应用/原生进程；原生内部串行）。旧测试断言与资源守卫未删除，CI保持停用。物理MVS、客户RCS、真实PDA/扬声器、目标IPC安装、现场30分钟磁盘/吞吐及长稳NOT_RUN。本包不继承PR175对其他会话中特定本机数据的额外操作授权。

后续证据提交不修改本次产品/测试/脚本字节；实际合并以PR记录为准，main树核对与合并后实跑另记，不能将这里569项改标为main实跑。
