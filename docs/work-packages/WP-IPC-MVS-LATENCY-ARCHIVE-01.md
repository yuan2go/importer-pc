# MVS-LATENCY-ARCHIVE-01

2026-10-30，处理[PR157归档P1意见](https://github.com/yuan2go/importer-pc/pull/157#discussion_r4414737632)。实施基线main `9df3901ec20d73512d48920b4671b529ebc58a9e`；不重复时延调查、不覆盖当前文档或既有归档。

从首次修改提交`14eead38`的父提交`f870ffc0`恢复`docs/INDEX.md`与`docs/runtime/mvs-software-reader-01.md`的完整Git Blob字节，记录完整提交/Blob/SHA256/长度，设置不作文本转换，新增[归档索引和清单](../archive/pre-mvs-latency-01/README.md)，同步根manifest。

仅验证来源关系、暂存Blob与二进制字节、独立检出、归档索引引用、当前文档/性能证据未变及完整manifest。构建、模拟、真实相机、客户RCS和现场性能均NOT_RUN；不扩张已有PASS或修改D/RV。PR157已经合并，修复经后续main PR提交，不向合并过的分支推送来冒充有效修复。
