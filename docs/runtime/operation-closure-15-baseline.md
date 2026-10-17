# OPERATION-CLOSURE-15 执行基线与计划

**Goal:** 闭合每次接收责任、箱反馈、连续输入、维护及追溯与受控旧部署升级。

**Architecture:** 保持Platform机制、Supply业务权威、Desktop应用契约、Product网络/资源所有权。接收以每次外部调用的持久身份表达，原件Flush后T3原子确认该次责任，绝不按请求清除另一接收。UI只投影事实与应用守卫。

**Tech Stack:** 既有.NET10.0.402 / runtime10.0.13，WPF，SQLite；不新增依赖。

## 有效范围与来源

- 执行起点main `5548b3f962c409079618635d3128830ea7887069`；原工作区HEAD0096f34，有5组未跟踪受限/任务资料，未移动/覆盖。远端开放PR为空；独立工作树/分支`codex/operation-closure-15`。0096→5548仅文档，没有源码差异。FIX14冻结68cdc36运行证据按原范围承接。
- 已完整读取AGENTS、INDEX、15、当前25问题、PR14后分析、framework/ADR/controlled-extensions、12/13/14任务与14报告/checks/证据、13复核、operations/developer-guide、D/technical/RV/公开边界；v1.1 README/GALLERY/完整交接及原HTML内嵌CSS/JS与分离源码一致，已读源码/tokens并查看正常/记录/详情实物。10/12/13映射与14后验继承。
- 批准范围：本包内部工程决策、合成验证、开发分支/main PR；保留原请求/唯一接受账、权限/CAS、精确包身份、资源和可见性保护。F-01～12有效；后装和Windows后验按12/13后续决定。
- 现用待外部批准：模拟实体/数量/身份策略；真实SDK/WCS/现场能力仍缺。D-01～16及原RV-01～17不倒写，原PDF固定8cab。Inspection及既有通知/健康不重建。
- 历史：14原子问题已有核心证据；新增A01-R1/R2、A04-R1、忙态输入及UI残余仍未关闭。原件/失败/SQL/checksum/候选保留；已正确入口只改执行时态。
- 原字节与替代关系：[前置归档](../archive/pre-operation-closure-15/README.md)。windows-06叠加“14待修/11当前候选”等头部转为历史，W01～06要求保留。operations/扩展指南明确14起点，不把15目标写成实现。
- 主机macOS26.5 ARM64，固定SDK已核验；无既有获授权Windows交互环境记录，WPF实际截图0，Windows/HID/声音/ACL NOT_RUN。真实集成与现场BLOCKED_PER_CAPABILITY。

## 执行切片（按15顺序，持续实施）

### P0 当前指引

- [x] 核验版本/PR/工作区；读取证据与实物；保留原字节后清理冲突，先提交文档。

### P1 每次接收与T3

文件：Supply/ResponsePersistence、Store/Maintenance/HistoryQueries/ExtensionMaintenance；Supply.Contracts/Model；Product/SenderModule、ProductRuntime；Verifier。

- [x] 新增前向format5接收发生表，旧1～4 SQL/checksum不改。BeginReceiptAsync在真实Execute/Query前持久预登记；失败则不外呼。原件按发生ID接管；Complete携带同ID，T3原子保存响应/冲突/完成证据；无响应显式结束该次接收，外部结果Unknown仍独立保留。
- [x] 重启逐次重放，已提交T3可确认完成，缺原件的真正不确定接收持久隔离并给原请求/发生诊断；旧围栏作为legacy未知证据保留，不推测所有接收已取得。读者/清理/维护/包保留均同源。
- [x] 确定性Q1创建失败/Q2成功，Q1Flush+SQLite回滚/Q2创建失败；重启、AfterT3中断、真正无raw、并发Product查询与停止所有权。保留同EvidenceId冲突/迟到/唯一接受原断言。

### P2 箱反馈与可用性

文件：Contracts/Model、Supply投影；Desktop/ViewModels、MainWindow.xaml/.cs/Closure。

- [x] 持久箱级Context/CheckClose/Close结果及身份/原因，匹配原Session；当前状态与最后结果分别显示。应用Guard理由、主服务/来源/投影时效就近可见，Accepted事实不改。
- [x] 验证三类拒绝、旧箱/两通道交错与重启归属、权限和过期策略。

### P3 输入与维护

文件：Desktop/ScanInputDraft、LaneControl、ConfigurationDraft、ExtensionsView/MainWindow.Extensions。

- [x] 有限接收隔离：当前请求期间禁止向已提交文本追加，忙态输入显式拒收并提示重扫，不自动排队；结束符明确反馈且不自动提交；失败保持可重试、迟到不能消费新草稿/新作业。实际SendAsync完成路径调用同一策略。
- [x] 局部目标读取不Build整草稿，保存/预览严格校验不放松；选择代次覆盖成功/错误/finally、导入/新建/身份失效。验证A慢失败/B快成功以及多实例导入编辑/保存/CAS/直接激活/重启。

### P4 追溯与动作

文件：Desktop/HistoryPresentation、MainWindow.xaml/Closure/HistoryExport/Presentation、LaneControl/Themes。

- [x] 会话与Item字段分开；列表占全宽，详情按需并可返回，保留本窗条件/选区/滚动/分页和迟到保护。
- [x] 可见筛选标签、本地日期时间/显式时区、严格范围反馈；默认列先显示结果/接受量/对象/码/尝试摘要，技术详情按需展开。
- [x] 详情改次动作、移除重复入口，ReviewFits只检查当前必要事实/动作，关键矩形不可见仍保护；复用tokens/字体。

### P5 旧部署

文件：实际支持配置/维护路径、必要脚本和指南；Verifier或独立合成旅程。

- [x] 保留13匹配宿主/1.0.1原包；清偿责任、维护停用形成合法过渡Effective，备份显式升级、安装新精确包/保存激活/启动、供包追溯停止重启；失败保旧、旧读者拒绝。无授权/能力时只阻塞实际缺口。

### P6 同版验证和交付

- [x] 定向--operation-closure新反例；受影响--baseline/--conformance/--followup/--closure与必要资源/包检查；locked restore、单节点构建含WPF。
- [x] 源码冻结后生成portable/win-x64必要同版候选并解压核心/插件冒烟，记录完整命令/日志/标记/hash。公共DLL变化须新包版本，不重签旧包。
- [x] 逐AC/问题矩阵、UI映射、部署/恢复、manifest及Windows后验场景；实际客户端缺环境逐项NOT_RUN。提交推送并创建main PR，不合并。

执行中只因证据调整细节；不新增第二业务权威、通用解锁、自动重发或模拟生产成功。必要检查失败定位修复并保留原输出，不反复跑无关全量。

实施与实际证据见[15报告](operation-closure-15.md)。勾选表示本切片授权工程工作及证据登记已完成，Windows实际后验仍NOT_RUN，外部能力按项BLOCKED，PR不自行合并。
