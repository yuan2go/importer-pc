# UX既有成果不测试发布与全部PR合并

用户本轮明确要求提交推送代码、报告、文档及刚刚任务产物，并实际合并全部开放PR。测试、构建、打包、启动和仓库验证器均为 **NOT_RUN_BY_USER_REQUEST**，Git hooks禁用。本地受限客户原稿、私密配置、运行lease及重复文档未上传，无关工作树保持。

## 固定来源与合并方式

开工远端main `eeeed0e0700cd07a76587b2bb701e91d422b2c35`。PR37原head `2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94`；PR38 head `d9333543b29cb35bae5b80d4db92e1d65cf366da`；PR39原head `24d2650a61caf0f5f147daf6ed67789e33b65c4d`，Git祖先已包含原PR37全部提交。

PR38已按精确head合并为`53bcb798853c0c6a0e3b6c5a0c2a85a3001a7103`。PR37纳入最新main/skill与审查材料后，后继head `1d486b34d3e3bcce2c6f0040a347bd1a5264024c`实际合并为`07095fdf4e04519934076ede24bfa8607207f9bc`。PR39在此基础上继承并补差额，不关闭或用其他PR替代原PR。最终merge身份及唯一提交数随后按Git/GitHub实际结果记录。

两次分支整合的冲突均仅为manifest JSON及其摘要，无源代码/页面冲突。第一次清单同时保留PR37成果、main的原UX审查输入和PR38的MIT skill来源/文件；第二次保留PR39最终实现、两边任务记录与skill完整库存。路径、原head及原PR描述见[PR37裁决](evidence/ux-publication-20261019/merge-37.json)和[PR39裁决](evidence/ux-publication-20261019/merge-39.json)。

运行、配置、脚本、测试、根构建输入和随包指南保持PR39最终字节。仅README、INDEX及框架当前入口把初轮1.3.1/PR37未合并说明同步为实际最终1.3.2，原字节可由记录的原提交/blob读取；不改原形成时结果。项目skill固定上游56ea9cf174ed86786187daa9de0e6247cb0747e4，MIT及UPSTREAM清单保留，不覆盖项目或客户许可。此次未执行其搜索、自测或数据检查命令。

## 执行产物与验收边界

[最终参考预发布1.3.2](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-6ce1d6d)已有15个附件，固定构建源码`6ce1d6d0b109bd6a3d9490fac176122b4670b90b`；包括5ZIP、3IPCX、3身份文件、公钥参考信任、原验证证据ZIP、ARTIFACTS及SHA256SUMS。原字节不重建、不重签、不覆盖，旧f1a655c发布及203c1a8/7036488未发布本地尝试保持原身份。

[原PR37报告](ux-commercial-fix-01-results.md)、[WP-01～07结果](ux-commercial-closure-02-results.md)、[最终续修报告](ux-commercial-rereview-03-results.md)、原PDF/HTML/图册、D/RV及历史267项资产错误均保留。原PASS/FAIL不是本轮新运行结果。Windows原生交互、控件/DPI/HID/安装及目标负载 **NOT_RUN**；真实SDK、权威Unknown合同及商业组合仍受阻，商用与现场未放行。软件暂停不是安全急停。

## 实际合并、远端产物与数量截止点

PR39后继head `5866fae21ace88743aa6e7e8aa0e43bb499236d7`已实际合并为`f1549822683b64d858349af0867e62ba92432d75`。开始时3个开放PR37/38/39全部MERGED，截止该main时Open PR为0。相对开工远端main，唯一提交22笔（16普通、6合并）；PR37与PR39共享的提交只计一次，不相加各分支ahead。

本最终回执另经独立PR增加1个普通提交和1个GitHub merge，合并后总计24＝17普通＋7合并。其中纳入原任务唯一17笔，本发布流程新建7笔＝1笔回执内容＋2笔分支整合＋4笔GitHub PR merge。最后实际main、回执PR及总数由Git/GitHub读取，避免自引用提交SHA。

[最终机器回执](evidence/ux-publication-20261019/final-publication-receipt.json)逐项记录原/最终head、实际merge和15项资产。此次重新读取GitHub state、长度和SHA256，与原PR39冻结回执全部一致；未下载重验、重新打包或新签名。skill的原许可证、UPSTREAM及全部文件入清单；原生/真实合同和商用门禁没有因Git合并或资产回读关闭。
