# PR73 代码、报告与既有产物发布

本轮基线为最新 main `2c2b5f137084eedfd30800fb5b1b7262b4560800`。用户要求不测试，提交并推送代码、报告、文档及刚刚任务的执行产物，创建PR并合并全部开放PR。开工原工作区干净，`codex/sportmaster-parameter-refactor` 的 `5fca596f552a1cd53fdc23ed2853b075869a2383` 已推送，[PR73](https://github.com/yuan2go/importer-pc/pull/73)是当时唯一开放PR；无需为同一代码重复提交或重复建PR。

在独立集成工作树合入main。生产代码没有冲突，冲突为索引、设计裁决、实施记录、工作包、V1阅读入口及manifest。保留main新增V2原型/PNG/交接/字段迁移等全部原件和V2目标；继承PR73代码及原构建、配置恢复、Windows合成证据。原PARAM-01范围完成与V2新增字段归属、五步首配、三组相机任务信息等待实现范围分别说明；保留ba80形成时静态对照，不把旧快照描述继续作为当前结论。

本轮不运行测试、构建、打包、安装、启动、UI操作、SDK或真实设备调用。原报告PASS仅适用于其原提交/夹具；最终合并树没有新增运行验证。真实海康SDK/设备、RCS合同与联调、目标工控机DPI/触摸、完整安装升级和现场仍按原记录待验。原D批准、RV与冻结请求语义不改写。

原便携CLI Simulation候选的SourceCommit为 `220d35931a6e2174d35629a65a01a1b61cc39b21`，业务1.5.0；两个TestOnly扩展的PluginSourceCommit为 `5cd89bd6a1a89004a663b62d25b1f707acd8ce1c`。它们按原字节上传新独立Release，不覆盖旧版、不改标为最终main或V2实现、不作为新的Windows安装包。身份及历史验证见[PARAM实施记录](parameter-refactor-01.md)。

仅上传获授权代码、项目报告/文档、合成证据及上述已有产物；不上传报价表、客户原稿、凭据、私钥、测试信任、SDK或生产数据。原工作区不切分支、不重置、不写索引；不通知其他任务。manifest仅更新暂存Git原字节的大小、SHA256和blob，保留main新增V2记录；删除PR73已经查明的两项过期InstallerWindow当前清单条目，原文件历史保留。

## 已上传的原执行产物

[下载Release](https://github.com/yuan2go/importer-pc/releases/tag/parameters-sm-v1.5.0-220d35931a6e)已上传原文件，远端大小和SHA256回执与原交付身份一致：

| 文件 | 字节数 | 原SourceCommit | SHA256 |
|---|---:|---|---|
| release-220d359-final.zip | 18568606 | 220d35931a6e2174d35629a65a01a1b61cc39b21 | 0f78368ed20ed2a8f3898eaf353c239a0347997e60b332a6d82e56a91f5e96f6 |
| sportmaster-rcs-task-1.2.1.ipcx | 23854 | 5cd89bd6a1a89004a663b62d25b1f707acd8ce1c | da02fde4ce44aa2074a75f878823eb70eb421819372ead3d879c6997119c43c3 |
| hikrobot-idmvs-reader-1.0.3.ipcx | 21086 | 5cd89bd6a1a89004a663b62d25b1f707acd8ce1c | ac1aeaa2177819cd06dc1673955b7cf73163ebac9ae7534a66e796f3a0b5e319 |

对应的release-identity、两个扩展identity、PUBLICATION及SHA256SUMS同时上传；原始[上传回执](evidence/publication-pr73-20261020/artifact-upload-receipts.json)与[本轮范围记录](evidence/publication-pr73-20261020/publication-scope.json)入库。ZIP是便携CLI Simulation候选，两个扩展为TestOnly，身份核对只读取归档/JSON及上传回执，没有运行产物。

## 提交与计数方式

PR73原有7个相对本轮main未合并的提交；本轮协调main的合并提交为 `168d56d95753a6a4e89663d8f14bdab4c7410a41`，本提交再记录原产物上传。最终GitHub合并结果及去重提交数在合并后读取，不预先编造合并SHA。计数以本页基线至最终origin/main的Git祖先差集为准，不累加分支ahead数。原工作区的代码和测试没有由本轮新增修改或执行。
