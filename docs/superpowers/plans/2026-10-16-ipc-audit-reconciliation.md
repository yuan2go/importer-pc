# WP-IPC-AUDIT-RECONCILIATION-01 执行计划

> 执行方式：沿现有模块化单体增量实施；按 writing-plans / executing-plans 的任务隔离建议，将桌面落实与验证资产交给独立子代理，主代理负责应用权限、最终配置所有权、集成交付和最终复核。

**Goal:** 实施08、10及09，以同一源提交形成合成参考闭环和可运行包，06/05按实际环境分项报告。

**Architecture:** 保留Supply单业务所有者、Product启动/来源所有权、SQLite3和SIMULATION-v1原请求；Desktop仅消费应用投影和命令。先取得数据租约并选定最终配置，再取得实际资源集合租约，最后启源；权限在应用所有者内统一判断。

**Tech Stack:** 既有C#/.NET10.0.402、WPF、GenericHost、SQLite、独立loopback。

## 全局约束与已决实现方式

- 本次用户明确授权合成/参考测试、构建、发布和启动；原不测试/NOT_RUN快照已按原字节保存在docs/archive/pre-reconciliation-01，不倒写历史。
- 最新main 856e4de；隔离工作树与codex开发分支；不覆盖主工作区未跟踪客户材料；本次开始无开放PR。
- 不改目标基线、D/RV、SQL迁移checksum、原协议/请求/目标；Production缺能力继续拒绝。
- 宽屏和紧凑保留双通道关键事实与固定动作；低频页用可返回抽屉，工程控制独立。客户区不足1280x680阻断新动作，恢复空间后需明确恢复；不整页缩字。普通供包直接发精确owner/revision命令，清空/关闭等风险保留明确确认。
- 当前诊断保持可发现；全部历史/详情读入口以有效模拟身份为准，无权时UI明确失效。

## 任务与检查点

- [ ] A 证据/入口：原字节快照及哈希，现行入口转向本计划与本次授权。业务改动前提交。
- [ ] B 08A：StationApplication全部历史/详情入口统一RequireIdentity；核验未登录/有效/退出/过期矩阵，桌面由UI任务实现失效。
- [ ] C 08B：ProductRuntime最终Effective/Pending选择与seed站点/模式校验，锁实际设备集合；失败激活保旧，部分取得失败无来源启动；新增针对性验证。
- [ ] D 10：Desktop按v1.1原HTML/CSS/JS/tokens/交接增量组件化，PAGE01-08、双通道数量/拆包、普通/风险确认、抽屉导航及焦点/布局守卫；保存逐页映射和未知字段来源边界。
- [ ] E 09：先对齐AlignmentDataVerification及HardeningVerification显式迁移3:3断言，保留历史checksum/失败保旧/旧及未来读者拒绝；补07风险和恢复防重/来源/提示/资源终态。先读Program选择器再按风险执行。
- [ ] F 集成运行：锁定SDK还原构建，运行受影响/必要基线及完整合成持续闭环；失败定位修复并仅重跑受影响模块。
- [ ] G 同版交付：提交最终运行源码，封存SourceCommit/Release/Storage/Contract/模块配置指纹与ZIP SHA；新包启动/发布冒烟。无Windows时06所有实际客户端项NOT_RUN并说明缺项；05逐能力BLOCKED。
- [ ] H 同步INDEX/runtime/operations/manifest及检查JSON，复核最新main/并行PR和差异，开发分支提交推送创建main PR并附加，不合并。

## 可复现命令入口

使用global.json固定SDK执行scripts/build.sh；Verification当前--alignment仍先运行Hardening，--hardening仅Hardening，无参数包含必要基线。新增选择器应保持旧默认兼容并明确输出实际选择范围。发布用scripts/publish.ps1（Windows）和本次跨平台封装脚本；真实Windows行为不以交叉编译代替。
