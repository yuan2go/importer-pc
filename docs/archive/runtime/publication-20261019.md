# 2026-10-19 提交、发行物发布与合并回执

按用户当次指令提交、推送已完成任务的代码、报告、文档和执行产物，并合并开工时全部开放PR。本发布流程的测试、构建、打包、启动验证均为 **NOT_RUN_BY_USER_REQUEST**；提交/推送禁用Git hooks。前轮实施验证保持形成时身份，不是本轮新验证。

## 代码与数量

开工远端main为 `4afe2afacde9e2acab7648fff3440fa3cc331b6e`，本地main原为 `2126ac2c06a0c352d96afbbf13ec1a719c352738`，已以fast-forward同步。开工只有 [PR26](https://github.com/yuan2go/importer-pc/pull/26) 开放，现已按精确head `78298b5dc7ccdcb7bb08e857d2d94f3681182fde` 合并；GitHub合并提交 `9a5d87ad41e3e936f56a7e58ec60de8f7aa53458`。

PR26相对开工远端main新增 **8个分支提交：7个普通提交、1个分支整合提交**；另有1个GitHub PR合并提交。本回执通过独立PR增加1个普通文档提交；该PR合并后，相对开工远端main共新增 **11个提交：8个普通提交、1个分支整合提交、2个GitHub PR合并提交**。本回执PR及最终main身份读取Git/GitHub，避免把构建源码、证据提交和合并提交混为同一身份。

## 执行产物

[参考预发布：8个原字节附件](https://github.com/yuan2go/importer-pc/releases/tag/audit02-rereview-reference-e914e75)绑定原构建源码 `e914e75a109672cd1d52be91c40459f09231ea57`：3个ZIP（portable核心、portable选装、win-x64核心）、reader 1.1.1与能力1.8.1的2个ipcx、2份原身份文件及SHA256SUMS。GitHub资产摘要与本地原件一致；没有重建或重新打包。全部资产URL、长度与SHA256见[机器回执](publication-20261019.json)。

最终任务报告和完整公开证据已随PR26进入main：[逐项结果及恢复步骤](architecture-quality-audit-02-results.md)、[原失败/中间候选/最终证据](evidence/architecture-quality-audit-02-rereview/README.md)、[维护指南](architecture-quality-closure-01.md)。历史267项资料错误及并发失败/后续复验均保留原范围，不据合并改写PASS。

## 范围

Windows实机 **NOT_RUN**；真实SDK、协议、设备及现场 **BLOCKED**，Production仍 **NotReady**。Simulation参考扩展进入媒介不等于安装、生效或生产放行。

其他仍在进行的UI、RCS与兼容性工作树未改；本地受限客户原稿/归档、运行lease和已有公开副本的本地重复文档未上传。签名私钥、信任配置、SDK/字体及生产数据不在发行物内。仓库可见性未改，不强推、不绕过保护。
