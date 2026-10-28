# 整合前验证入口修正

2026-10-29 用户更新整合规则：一般技术冲突/验证失败须依任务合同定位、修正并重新验证后继续，不再失败即停；SDK-only、原件保全、单写者、禁止强推/绕过保护及业务语义裁决边界继续有效。此前停止记录保留其当时规则与失败证据。

基线为main `9e5cd3bc37045554252b03ce1bb5a986e51dd82d`。用户已批准整合评估方案，先处理默认验证入口已复现的问题，再按顺序整合PR；供包台相机生产路线只保留SDK。此包不改业务代码、数据库格式、冻结原件或生产准入。

| 验收 | 修正与边界 |
|---|---|
| 取消后意外回执 | 按CF14/OC15保留CancelledBeforeDispatch，矛盾原件和接收发生另记责任；逐请求种类核对完整hash、原身份、原终态、权威响应为空、零发送、零接受、冲突阻断和恢复可发现性。不得改回Unknown以满足旧测试。 |
| 历史JSON夹具 | 仅比较时统一JSON缩进换行；仍严格比较完整字段/结构并核对未改动原PayloadHash。运行时Wire、原文件和原报文字节不变。 |
| Windows默认Application入口 | 缺少受保护管理服务时接受该精确Production拒绝，明确不是已安装服务或真实SDK资格；不关闭守卫、不改变机器策略。其他异常继续失败。 |
| 当前manifest身份 | 生成器默认保留现有work_package，可显式指定；保存原记录顺序、继承来源和冻结字段，按staged Git blob重算。--check继续核对完整路径集合/内容/hash。 |
| 损坏库拒绝 | 完整入口AC-01/03/10与current-contracts共享同一检查：预检抛InvalidDataException且保留内部SQLite错误26；原文件逐字节不变，不产生WAL、SHM、journal或替代文件。不恢复旧异常合同，不修改业务预检。 |

命令沿现有Actions：Release/locked完整解决方案、Debug Fixture、无参数Application及Supply默认入口、Supply current-contracts。首次失败日志在仓库外D盘原审计目录保留；本包验证结果在实际运行后记录，不借用旧PASS。原始[OC15合同](WP-IPC-OPERATION-CLOSURE-15.md)与[CF14记录](../runtime/conformance-fix-14.md)继续有效。

原冲突扫描命中历史Python日志的长等号分隔符，保留其原字节及原扫描输出；追加精确Git冲突标记检测，不能删除冻结证据以得到空输出。

首轮ac259244本机Windows验证：Release/locked完整解决方案构建退出0、0警告、0错误；Debug Fixture构建退出0。普通令牌Application默认入口退出0、80个PASS输出行；Supply current-contracts退出0、13个PASS输出行。输出行数不是独立断言/用例总数，这些入口未提供标准化跳过计数。两处H04原失败现均通过。

首轮CI测试合并树6fc0ba1a在AC-01/03/10暴露另一处旧异常类型断言：原测试期待SqliteException，当前Supply6只读预检将其包装为InvalidDataException。用户要求解决后，本包将同一损坏库原件保护检查同时接入完整入口和current-contracts，避免选择器再漏检。首轮失败记录保留；后续完整CI以修正提交对应的实际结果为准。

损坏库断言修正的提交前Windows验证：完整Release/locked构建退出0、0警告/0错误；普通令牌current-contracts退出0、16个PASS输出行，含新增三项拒绝/原字节/无新增文件检查。独立baseline入口本机先被既有机器租约策略拒绝，未到达末尾用例；未改机器策略，完整入口仍交本提交对应Ubuntu CI核验。这里不将提交前结果冒充已完成的最终main验证。

Supply完整默认入口在本机尚未完成：普通令牌运行至链接拒绝夹具时因缺少创建符号链接特权而退出；提升令牌复核通过该链接拒绝，但后续真实ProductHost被现有普通启动身份守卫正确拒绝。保留两份失败日志，不修改机器策略或放宽守卫。已有Ubuntu current-contract-boundaries作业增加无筛选完整Supply入口，并保留完整日志和失败退出码；其结果以本次提交对应Actions为准，不能预记PASS。Windows安装冒烟、真实SDK/设备不在本包验证结论内。

PR133整合树3670c1f2的AC15代理曾在回写响应时出现Broken pipe，异常由收尾等待传播；日志未记录具体断开路由，不能倒写为已确认某一种网络时序。相关业务链路与夹具文件相对main未变。当前修正只作用于验证工程：测试代理复用本仓库ASP.NET Core宿主及真实RequestAborted，停止等待在途响应，超时失败并继续清理；查询门闩不会在清理中被放行。预期取消仅限已确认中断的健康请求；Q1/Q2失败及未知错误记录方法/路径/阶段后继续使验证失败。测试工程单独增加框架引用，不改变生产依赖。

`--query-fixture`提供受控回归并纳入完整入口和CI：chunked POST实际到达模拟接收方后丢响应、在途响应排空、真实健康请求取消及后续可用、查询取消必须失败、非预期健康/查询异常必须失败。原Q1/Q2回执、Accepted、重启RawUnavailable和禁止准入断言保留，新增两条查询响应均完成及零在途handler检查。故障注入会输出QUERY_FIXTURE_FAILURE后断言它确实传播，不能将该诊断行单独解释为测试失败。网络实际写入错误的所有平台变体及构造失败路径未宣称逐一验证。

本轮修正工作树的本机Windows验证：完整Sportmaster解决方案Release/锁定还原构建退出0、0警告0错误；`--query-fixture`9条断言加入口完成行均PASS、退出0；`--current-contracts`15条断言加入口完成行均PASS、退出0。对应日志保全于D盘整合执行目录；完整Supply及原AC15产品宿主路径以新提交的Linux CI结果另记，不将本机夹具通过扩张到真实设备、安装验收或所有网络故障类型。
