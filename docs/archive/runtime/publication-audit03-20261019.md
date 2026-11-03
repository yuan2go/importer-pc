# 审查03既有成果不测试发布与合并

用户本轮要求提交、推送代码/报告/文档及刚刚任务执行产物，实际合并所有开放PR；测试、构建、打包、启动验证均为 **NOT_RUN_BY_USER_REQUEST**，Git hooks禁用。当前工作区与其他原工作树保留，受限客户资料、私密配置、运行lease和本地重复文档未上传。

## 固定输入与合并裁决

开工远端main `7cf84f371dc0076231bde7a25e5d4f22404db93b`；[PR34](https://github.com/yuan2go/importer-pc/pull/34)原head `c07304418aa68428331ba141418edfb3519a6939`，[PR35](https://github.com/yuan2go/importer-pc/pull/35)原head `ec5b3d675e17a59b3d1f198d4aae24b306d1a89b`。PR35已按精确head合并为 `97a01fb02e7fef6b9c3a769092d57728b992489f`；PR34通过后继整合提交保留两边祖先后更新原PR，不关闭或替代原PR。最终实际merge身份从GitHub读取。

逐文件比对PR34的改动与PR35最终源码：当前件定位、终态恢复/原件守卫及CommercialQuality/SportmasterUi测试已被PR35继承；快照部分在PR35增加单调时效、退役实例/修订倒退保护、过期格口标记和独立暂停。保留这些扩展及PR35的SQL7永久编号、原模式备份与交付入口，不能回退到旧1.2.5/DB6，也不重复拼接已有代码。

25个冲突按版本/实现/当前指南/manifest分别裁决，完整路径及双方Git blob身份见[裁决记录](evidence/publication-audit03-20261019/merge-resolution.json)。保留PR35的业务1.2.8、Contracts1.8.0、DB7和精确参考包声明；运行、配置、脚本、测试及随包指南保持PR35最终字节。PR34独有工作包、完整结果、原验证脚本与摘要入库；有效Windows06历史说明保留。框架两处当前版本仍为1.2.7，已仅同步声明为实际1.2.8，不改历史段落；原字节与PR34候选设计见[归档](../archive/publication-audit03-20261019/README.md)。

## 已有产物与验证边界

[PR35最终参考预发布](https://github.com/yuan2go/importer-pc/releases/tag/audit03-reference-565abcc)已包含16项附件：7个ZIP、3个ipcx、3份身份文件、仅含公钥的参考信任文件、ARTIFACTS与SHA256SUMS。固定构建源码 `565abcc63b885dc60873aa21e25f2d4612963c0d`；本轮不重建、不重签或覆盖原资产。1401b7d及PR34旧候选保留形成时身份，不能据合并升级资格。

历史验证见[PR35逐包报告](commercial-audit03-delivery-results.md)及[PR34原整改结果](commercial-quality-fix-03-results.md)。原PASS/FAIL、267项历史资产错误、原PDF/ZIP、D/RV和未完成项保持形成时边界。Windows实际仍 **NOT_RUN**；真实SDK/协议/设备/现场 **BLOCKED**，指定商用组合不通过。此次仅检查Git内容/身份与远端发布状态，不授予新的运行或现场PASS。

## 实际合并与最终计数

PR35实际merge为`97a01fb02e7fef6b9c3a769092d57728b992489f`；PR34后继整合head为`a71c6d7dea166b33c94be863ebd1b08c2be1f565`，实际merge为`a0bec28bcb6fef817004661b7dd2782a86676370`。两PR均MERGED，截止该main时Open PR为0。相对开工远端main，唯一提交数为16（13个普通提交、3个合并提交）；没有按各分支ahead重复相加。

本最终回执另经独立PR增加1个普通提交及1个GitHub合并提交，合并后总数为18＝14普通＋4合并；其中既有任务13笔，本发布流程新建5笔（1笔回执内容、1笔分支整合及3笔GitHub PR合并）。最终实际SHA及总数以该回执PR合并后的Git/GitHub为准。

[最终机器回执](evidence/publication-audit03-20261019/final-publication-receipt.json)保留两PR头/merge、计数基线和16项远端资产。重新读取GitHub资产状态、长度和摘要，与PR35已有冻结回执全部一致；未重复执行原运行旅程、打包或签名。
