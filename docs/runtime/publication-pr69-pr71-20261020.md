# 本轮代码、报告和既有产物发布

本轮起点为 main `6784d08a7a8f5326607dda83e00d21bc2ed71632`。用户明确不测试，提交代码、报告、文档及既有执行产物，创建 PR 并合并所有开放 PR；并明确不通知其他任务，仅合并已经推送的当前快照。未上传根目录报价表、客户原稿、凭据、签名私钥或测试信任，未改变仓库可见性。

## 精确提交与合并边界

- [PR70](https://github.com/yuan2go/importer-pc/pull/70)：RCS心跳，原 head `20bd90f7488f6d0af4cc161baf45fd960ba54fc7`；已合并为 `0facaf9a14815c9762711066229eda9eea049fa9`。
- [PR69](https://github.com/yuan2go/importer-pc/pull/69)：安装器与固定桌面名称，原 head `1ad0307e019cbba2471d996de5b8c854895330ef`；合入 main 的索引及清单冲突只做并列保留、按 Git 原字节更新身份，协调提交 `7531bc1c5b6415aeb2a69bc74a286fbc5aea2d04`；已合并为 `c837b1c9ec3bf2fa3ff0290b998b7f6b384c000a`。
- [PR71](https://github.com/yuan2go/importer-pc/pull/71)：参数重构的冻结快照 `5abe82071471d525686352c094dbb79dc7fea3ce`。原任务仍在实施，本 PR 不承诺完整性或运行通过，快照之外后来继续写入的文件不纳入此次发布。采集范围见[快照记录](publication-parameter-snapshot-20261020.md)。

PR71与main合并时，代码自动合并，唯一文本冲突为文档索引，保留PARAM、安装器、心跳三者说明。按差异读取合并结果，安装单窗口准备、一次授权、安装原责任守卫与稳定桌面入口保留；参数编辑仍沿应用契约，不引入第二个业务实例。Git manifest按已暂存Git blob更新大小及SHA256，仅记录字节身份，不作为构建或测试通过证据。已获授权设计的`source/aux.js`保留原路径及原blob。

## 不测试约束与待完成工作

本轮没有运行测试、构建、打包、安装、启动、设备控制或原生UI验证。原任务历史PASS保留原提交和夹具身份，未扩大到最终合并树；参数重构报告仍为“实施中”。Windows图形、干净系统、真实SDK/设备/RCS及现场未取得的新证据保持未验证；旧D/RV不改写。

原工作区另有活动任务，本轮采用独立index采集及独立集成工作树，不切换或重置原工作区，不将其后续改动冒充已上传。本轮不生成包含最终合并树的新安装包。既有安装器和心跳介质必须继续标注各自原SourceCommit，不能改标为main或参数1.5版本。

## 已上传的执行产物及实际合并结果

PR71已实际合并为 `3af9065fb0634f39dd87bf82b106f3a2d46d13d9`。截至该提交，相对本轮起点 main 去重增加20个提交，其中13个普通提交、7个合并提交；不按各分支ahead数累加。该数尚不含本发布回执提交及其PR合并提交，最终数字在回执PR合并后重新读取。

- [原单窗口安装器Release](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.4.1-27b0d8f6cc02)：`CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe`，177478140字节，源码`27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`，SHA256 `062bd4878ff5f5c6c03fc7b0d01a026c59b55703c5d35c04f218eb961b4954e6`。不含后续固定桌面名称及参数快照。
- [原心跳Simulation产物Release](https://github.com/yuan2go/importer-pc/releases/tag/rcs-heartbeat-simulation-3f8a58ba3288)：源码`3f8a58ba32887df07f6a7817b64afa693f903c50`；便携CLI ZIP 18529462字节，SHA256 `f0491d1e02ce116703a233d1f3c560bb1969359a59ee92d7b5ae0a829535aba5`；RCS1.2.0合成签名夹具23862字节，SHA256 `b2c7d2e794630d7bcdde70df6f0b602d35528581792efb863b23de7c36857e44`。不是WPF安装包，不附私钥或测试信任，不能标为最终main产品包。

Release均使用新独立标签，未覆盖旧版本。仅比对既有文件的大小、SHA256与原交付记录，以及远端上传回执，不运行包内程序。公开上传大小/hash/下载链接见[远端产物回执](evidence/publication-pr69-pr71-20261020/artifact-upload-receipts.json)，合并与计数范围见[合并回执](evidence/publication-pr69-pr71-20261020/merge-receipt.json)。
