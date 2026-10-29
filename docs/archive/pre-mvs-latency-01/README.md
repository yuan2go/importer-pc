# PR157时延调查前的当前文档原件

2026-10-30补齐[PR157 P1审查](https://github.com/yuan2go/importer-pc/pull/157#discussion_r4414737632)指出的归档缺口。首次修改两份文档的是`14eead38b828af1e1b32b9be671f38015dbf78e6`；原件来自其准确父提交`f870ffc00abcba78497b3d01d83251d786052ae3`。这是合并后的补档，不代表原PR修改前已经完成归档。

| 修改前路径 | 原始字节快照 | Git Blob |
|---|---|---|
| `docs/INDEX.md` | [原索引](docs__INDEX.md.txt) | `e71814e3b6f0a33e5818afb1ed90f934f51ff5ee` |
| `docs/runtime/mvs-software-reader-01.md` | [原MVS指南](docs__runtime__mvs-software-reader-01.md.txt) | `4d6a0046228cfdad490d527f895b88e4594ecd44` |

完整来源、字节数和SHA256见[归档清单](manifest.json)。快照用`git cat-file blob`取得原始字节后直接二进制写入，不经过PowerShell文本管道、编码或换行转换；`.gitattributes`禁用本目录文本转换。暂存Blob及独立检出字节均与来源核对。

既有[合同收敛前归档](../pre-contract-convergence-02/README.md)是更早版本；[同帧留档前归档](../pre-sm-original-frame-01/manifest.json)来自`0f820f92`，已经包含本次时延调查修改。二者原样保留，不冒充本轮修改前原件。

当前有效[索引](../../INDEX.md)、[MVS指南](../../runtime/mvs-software-reader-01.md)、[时延结论](../../runtime/mvs-latency-01.md)与性能证据均不修改。本目录只作历史追溯，快照内部相对链接属于原字节，不作为本目录操作入口，也不重写来迁就快照位置。

PR157已于`9df3901ec20d73512d48920b4671b529ebc58a9e`合入main，不能再向该已合并PR增加有效补丁；本次通过后续修复PR交付。只验证来源、字节、引用、属性和manifest，不重新运行软件、性能或真实设备验证，不新增真机资格。工作包见[归档修复](../../work-packages/WP-IPC-MVS-LATENCY-ARCHIVE-01.md)。
