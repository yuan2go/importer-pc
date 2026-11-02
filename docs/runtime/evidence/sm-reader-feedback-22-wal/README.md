# PR177 WAL增量整合验证

最终产品/测试/脚本源 `926e27008eea9bce7b2ffe0f7211fc9cfb08c39e`，main基线 `cf98e139fb31c0579f39617324f67175b00aec1f`。PR176合并前main又合入PR177，因此原ebb96686的19套569项[完整验证](../sm-reader-feedback-22-integration/README.md)保留原身份，本阶段只补实际增量。

`source-delta.json`记录精确Git源码比较：相对ebb96686，仅 `ParcelOfflineUnsubmitted.cs` 和 `ParcelProductMaintenance.cs` 两处生产文件发生变化，完整继承PR177提前Dispose私有WAL读连接、使用SQLite打开前原DB/WAL清单的修复。本轮读码/界面/原请求/码制/音频生产代码及其他产品文件均无变化；WAL夹具和复验脚本的增量分别留在Git中。

本源三个Release构建0警告0错误；6套178项PASS：应用5套165（独立终态1、读码反馈70、背压/资源20、停止21、实际WAL模式升级/关闭53），原生PC21集成13。`summary.json`、Core/Native分段及原始日志记录同一源实际命令和退出码。没有将原569项标为这个源重跑，也不把相互重叠的检查相加当作新断言总数。

`publish-smoke.json`与程序集哈希绑定同源no-build发布，实际EXE `--environment-report`退出0，版本 `1.0.0+926e27008eea9bce7b2ffe0f7211fc9cfb08c39e`，三段自有WAV完整。复验命令为D盘干净工作树中 `./scripts/verify-reader-feedback-22.ps1 -Section Core -WalIntegration` 后 `-Section Native -WalIntegration`；不带开关仍可运行完整套件。

此前负向/中间失败、完整UI截图、全码制和新声音证据各保持原源，见[初始证据](../sm-reader-feedback-22/README.md)及[PR175整合证据](../sm-reader-feedback-22-integration/README.md)。本阶段没有UI生产增量，不重复复制未变的截图；原生集成仍实际运行。`hashes.json`覆盖本目录除自身外全部证据，禁止换行转换并核对Git实际blob。

物理MVS、客户RCS、真实PDA/扬声器、安装/实际数据库更新、现场30分钟吞吐/磁盘及长稳NOT_RUN；CI保持停用。未继承其他会话对特定本机数据的安装/关闭授权。最终HEAD重新触发自动代码审查，合并状态及main实跑另记，不能将这些分支PASS倒写成main/现场验收。
