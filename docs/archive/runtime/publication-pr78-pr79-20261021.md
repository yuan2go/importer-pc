# PR78/79 不测试合并与已有产物发布

2026-10-21。本轮起点为最新 main `dfec787f36066f70ac417cfb79d958c35d529785`。用户要求不测试，提交推送当前代码、报告、文档及刚刚任务执行产物，通过PR合并main，并合并全部开放PR。

开工原工作区干净，当前`codex/pr73-startup-recovery`已提交推送到`26fea6f7e6ea1a61e03f44f59252af814b17c998`；开放PR为78和79。现有代码/报告已有提交与PR，不重复制造相同内容的提交或PR。在独立集成工作树处理，不切换/重置原工作区，不写原索引，不通知其他任务。

## 实际来源与合并边界

- [PR78](https://github.com/yuan2go/importer-pc/pull/78)：参数V3专注工作区与统一保存审阅，head `b4f5c87f6587a66feb9ea347ee8700c592f9b9b5`，已合并为 `2986733087c9cf2602658cab6c8629773fefdcda`。V3设计原型、27张PNG、交接、字段迁移及原交叉构建记录继承main原件；[实施记录](parameter-settings-experience-02.md)的Windows原生、工控机/DPI与现场未验边界保持。
- [PR79](https://github.com/yuan2go/importer-pc/pull/79)：启动路由保留原Effective回退、修正进度文字及原CI闭环，原head `26fea6f7e6ea1a61e03f44f59252af814b17c998`。与PR78生产代码没有冲突，仅manifest冲突；清单按路径保留双方原来源/状态并更新暂存Git字节身份。原[修复/验证报告](pr73-review-followup-01.md)的“并行PR78未合并、待授权合并”等为形成时历史，以本发布记录的实际合并进展为准。

原PR79 CI成功属于原head与原GitHub合并树，不是加入V3之后的新验证。V3没有因此取得原生Windows或现场PASS。PR74/76已实现业务合同、登录角色及维护授权、唯一配置账/CAS、冻结请求、心跳、安装与历史D/RV均继承，不扩大原验收。

## 本轮不测试与已有执行产物

本轮没有运行测试、构建、打包、安装、启动、UI验证、SDK/设备调用；只做Git合并、内容/身份读取、上传及远端结果核对。原测试源码和既有证据保留，不删断言或改写失败。现有1.6.1安装包保持原SourceCommit，不改标为包含本轮V3/启动回退的新包。

原CI artifact `12284432859`（run `39465324737`）ZIP为1895433字节，SHA256 `00ddae3b8d4cedd9e3ffdc5cd786bc4415342d33c3a0953709f00708345f811d`。ZIP内46个文件是合成Windows截图、日志和回执，没有安装包或程序载荷。原PR head `5d722f8a4eebaa3a7d034c9c6becbfe8f63f645b`，实际受验CI合并树 `5edfe080157d415679a4798918589f64d4dcfe41`，两者分别记录。原production门禁FAIL为预期拒绝，不能改成Production通过。

仅按原字节发布新独立证据Release，不覆盖旧版。不上传报价表、客户原稿、生产数据、SDK/字体、私钥、测试信任、凭据或私密访问链接，不改变仓库可见性。公开范围与旧D/RV状态继续有效。

实际上传回执在本次PR内登记；最终PR79合并SHA、全部开放PR状态及去重提交数在合并后读取。计数范围为本页起点至最终origin/main的Git祖先差集，不累加各分支ahead数。

## 已上传回执

[原CI证据下载](https://github.com/yuan2go/importer-pc/releases/tag/pr79-ci-evidence-5d722f8a4eeb)已完成；远端上传回执确认原ZIP大小和SHA256一致。SOURCE-IDENTITY、原remote-ci-result、PUBLICATION和SHA256SUMS同时上传，见[上传回执](evidence/publication-pr78-pr79-20261021/artifact-upload-receipts.json)。标签指向原PR head，CI实际受验合并树另由SOURCE-IDENTITY明确记录；不是新版安装包，也不代表最终main新验证。
