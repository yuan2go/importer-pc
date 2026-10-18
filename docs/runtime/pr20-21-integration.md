# PR20 / PR21 协调整合 · 2026-10-18

用户本轮授权修复、复核、合并两项PR，取代两个工作包形成时“只交PR、不合并”的本次交付限制；原工作包和历史验收保持形成时事实。开工main `08286542bbd007433d85be2cb8d129b366b007e0`，PR20 `ecdbddc6640249347c7821cf3b1e58ccd7c22f83`，PR21 `e49249c2a5148544cdc5b7d8bf0a74a69c015026`。主目录未跟踪资料保留，隔离工作树顺序执行，不操作真实设备。

## 设计与执行计划

- [x] 读取最新main入口、framework/decisions、两个工作包与实际评审线程；以原请求、接受事实、独立回执责任和资源生命周期为不变量。
- [x] PR20链接路径负例先红灯；备份对源目录及目标所有已存在祖先拒绝符号链接/重解析点（含Junction、悬空链接），覆盖新父目录场景。普通外部路径仍允许。维护者须提供普通路径并保护祖先目录权限；检查不声称抵御有权限并发替换祖先的攻击。macOS的/tmp、/var等系统别名也受此规则约束，使用实际路径。
- [ ] PR21真实提供者ProtocolEvidence由Python构建器与C#签名包验证器强制64位ASCII十六进制；协议证据与资格文件比较统一忽略十六进制字母大小写，保留原签名文档和包身份，不改签既有包。
- [ ] 合并逐文件核对Commands/Guards/Runtime/Stages/Recovery，保留20的健康预算/取消、恢复清单和21的独立重量/图像守卫、Production资格与迁移；必要交叉用例及解包进程旅程验证。
- [ ] 定向构建/测试与证据通过后，先正常合并PR20，再将main合入PR21，比较测试树与最终树；正常合并PR21，核对远端main和PR状态。禁止强推、绕过保护、自动合并。

已复现：新增BackupAsync实际入口回归在PR20原源码接受指向运行根的外部目录链接，返回BackupCreated（FAIL）。修复源码`1b6962e1909e1a33967fce8902eb001bec341d6c`定向构建0警告/0错误；同一--backup-health-recovery全组PASS，包含直接/嵌套/悬空链接拒绝、普通外部目录成功、原Unknown上传备份恢复、健康超时和取消、PLC接受事实保留。中间一次失败是悬空链接测试清理误用Directory.Delete，改为File.Delete，保留原失败日志。证据见[evidence](evidence/pr20-21-integration/README.md)。Windows Junction测试分支已加入，当前主机为macOS，实际Windows仍NOT_RUN。

## 验证边界

Windows执行/UI/UAC/ACL/Junction：NOT_RUN。真实SDK、秤/图像实体关联、WCS/PLC握手与现场验收：BLOCKED。生产资格的合成签名测试不构成批准；缺必需证据继续NotReady。原REQ/BR/CAPI/D/RV、SQL/checksum、历史失败和原包身份保留。当前报告为执行中的增量，完成结果在后续提交补充。
