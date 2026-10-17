# PDF 发布与 PR14 后复核证据

本次在 Linux x64 对 main `0096f3435cc5066a1b50836ef534ec24221a7e6f` 做只读源代码、Git 输入身份、已存运行证据及资料发布检查。业务代码修改0；业务测试/构建/重新打包/Windows实际运行均未在本轮执行。

- [source-input-review.json](source-input-review.json)：发布前167项运行、测试、配置、示例、脚本、根构建输入及两份随包运维指南；冻结源 `68cdc36` 至核查 main 无差异；与原8cab审查的增量另列。本次随后更新的是文档，不能把旧候选的随包指南冒称已包含新文档。
- [record-identity-review.json](record-identity-review.json)：19个 `final-*.record.json` 对应日志存在、SHA256全部匹配；声明 PASS 的非空完成标记存在。原3个FAIL保留，后继记录与原输出分别核对，不能挑选退出0代替完整结果。执行平台为原 macOS/ARM64，不改记为本次 Linux 执行。
- [archive-before.json](archive-before.json) 与 [archive-comparison.json](archive-comparison.json)：原资料检查已知267项历史源资产错误与本次新增错误分别记录；未改校验器、未扩大豁免。
- [after-t3-readonly-review.json](after-t3-readonly-review.json)：原合成库只读检查，一条Context请求为Accepted，接受账0条，存在保守围栏；不证明新的组合失败已经运行复现。
- [remote-context-before.json](remote-context-before.json)：本次main、已合并PR及普通文档发布授权边界。
- [publication-checks.json](publication-checks.json)：PDF、归档、当前入口、源代码零改动及发布输入的具体检查结果。

继承运行结果还须结合对应代码和断言阅读。新的接收责任残余路径、维护不完整草稿异常和扫码忙态问题来自本轮源路径复核；没有新故障注入或 Windows 复现时明确登记，不伪造动态验证。现有合成数据库中的 AfterT3 保守围栏可读检查由最新报告说明。

原日志、首次失败、探针、候选与数据库见 [FIX14证据](../conformance-fix-14/README.md)，本次不重复上传或改写它们。关闭与放行以 [最新复核](../../../reviews/audit-reconciliation-01/POST-PR14-ANALYSIS.zh-CN.md) 的范围为准。
