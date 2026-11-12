# 当前代码与验证边界

当前规则见 [AGENTS.md](../../AGENTS.md)。开发范围是 Sportmaster 及其必需的共享基座。历史工作包、安装记录和验证结果只说明当时来源提交的行为，不授权恢复已退出的功能。

## 格式与业务事实

- 只接受当前配置 schema3 和 Parcel DB14。非当前格式、基线身份或实际结构不符时，在写入前拒绝并保留原 DB/WAL、配置与回执。不提供旧格式迁移或接续；当前开发更新使用卸载清理后重新安装。
- 已发送请求、冻结报文及目标、已分配编号、原始回包、永久防重身份和历史 Unknown 是业务事实。旧格式拒绝、历史事实读取和原件保护不是兼容层，必须保留。
- 当前 RoundId、自动重新触发资格与原请求执行责任分开；一轮最多一次首次调用。可信换车解除当前轮次，原调用/回执独立收尾。详见 [轮次契约](../design/sm-parcel-rounds.md)。
- 配置保留完整候选、CAS 和 Pending/Effective；管理权限在执行时检查；取消、停止和释放须按实际责任收尾，不能伪报完成。
- SM 使用内置相机驱动及 RCS，无外置部署资格、配置指纹准入或全站持久调试隔离。管理窗口暂停自动供包及识码；关闭、到期与单次调试仍按当前管理窗口和资源归属处理。
- 完整备份、隔离恢复、单实例、资源租约、WAL/FULL、身份核验及存储故障守卫继续保留。备份计划辅助状态故障不等于业务库故障。

## 验证收敛原则

先追踪生产调用、装配、持久字段和外部协议，再判断测试。只删除已退出行为的测试、无独立故障价值的重复验证和仅锁定内部实现的断言；真实边界的坏格式、冲突、取消和原件不变断言继续保留。生产代码无业务调用者、仅用于旧测试时才删除，不按名称或年代判废。

自动 PR/main 验证入口是 [industrial-core.yml](../../.github/workflows/industrial-core.yml)：MSTest 调用真实 Application 验证器，另验子进程产品覆盖、文档、分析脚本、发布策略和 Windows 构建。[sportmaster-commercial.yml](../../.github/workflows/sportmaster-commercial.yml) 是手动扩展验证，包含离线恢复与 Windows 原生安装边界，不代替自动回归，也不签发现场资格。

[SM 离线相机运行库安装](../../.github/workflows/sm-runtime-install.yml) 另按安装器及运行库文件的路径变化触发，不会因普通测试或文档改动自动执行。SDK 与完整发布工作流是手动入口；构建矩阵中保留其他 Application 的既有编译检查，不表示恢复其功能开发。

失败先区分生产缺陷、过期预期和环境限制；不恢复废弃功能、不跳过有效测试、不削弱断言来保绿。构建、本机、CI、Windows 原生、真实 SDK、客户 RCS 与现场验收分别记录，未执行写“未运行”。历史结果保持原提交和原结论。

## 当前验证取舍

| 风险 | 当前验证与取舍 |
|---|---|
| 存储身份、旧格式和篡改 | `storage-schema-only` / `storage-baseline` 使用真实 Repository；保留旧 DB/WAL 拒绝、当前结构篡改、应用身份及原件不变。删除与历史结构快照相等、仅豁免指定新增表的约束；历史快照本身保留。 |
| 历史调试审计 | `debug-no-isolation-31` 核验旧审计原文及原请求保留、权限和新调试独立发送。启动不解释旧隔离记录，也不追加退役转换记录；历史帧关联字段仍用于证据读取，不能按名称删除。 |
| 输入、外呼与停止 | SM 输入只做短决策和持久交接，受管理异步执行不占用输入消费者等待网络。轮次结束不取消已尝试调用；准备但尚未尝试的意图不得再发送。`sm-round-lifecycle` 核验期限、A/B乱序和单次提交，恢复/ResourceRisks 保留实际调用及回执/资源排空责任；不恢复以慢 RCS 填满扫码队列的旧预期。 |
| 补录与结束策略 | `sm-round-lifecycle` 核验到位轮次超时后 PDA 补录成功超过3秒仍不追加请求、可信换车后才开放下一件，以及纯手动3秒对照、PDA失败取下点击与旧轮失败隔离。输入来源不改写轮次建立依据。 |
| 备份源与目标 | `backup-consistency` 经生产复制和分类链核验完好源/损坏目标只失败本次备份、原合法供包继续；真实源损坏触发原保护。异常类型和消息不是归属证据，完成标记及最近成功只随真实完成。 |
| 发送资源与证据 | `sm-dispatch-resources` 使用真实回环HTTP复现拥塞，在T2前有界公平等待、保留独立心跳、移出后零迟到外呼及真实停止排空；100轮是合成短测。`stability-analysis` 覆盖纯人工、混合/超时补录、跨窗调用与晚到回执，按身份和各自时间关联，缺项明确未知。 |
| 相机样本与资源 | `mvs-presence` 使用生产模型核验校准、连续性失效、重连以及只读原图回放；`mvid-reader` 保留同帧/原生缓冲/停止/断线边界。合成样本不授予真实相机或现场24/72小时资格。 |
| 配置与快照 | 检查 Pending/Effective、Conflict/Unknown、完整草稿、CAS、实际提交次数和动作可用性；不要求中文界面返回英文状态码或旧提示词。SM 可保存 Pending 不表示外部调用已排空；原回执恢复夹具在所有实际端口到达探测前边界后才继续，不重试已受理查询。磁盘余量用已有卷容量边界控制，不假设测试机磁盘一定小于固定容量。 |
| 验证入口 | 未知或退休选择器返回失败，不落入默认测试。手动 CI 使用内置 SM/MVID 验证；`parameter-refactor` 已包含 `parameter-inheritance`，同一次运行不重复执行；Windows `installer-options` 保留独立 COM 步骤。 |

跨 Application 的现有验证、真实协议拒绝和无法证明无效的检查保留。Windows 符号链接权限、受保护目录及安装授权属于环境条件，不能将其失败归为过期预期或通过放宽生产校验消除。

格式和版本见 [版本规则](version-compatibility-01.md)，运行见 [入门](getting-started.md)，备份分析证据见 [长稳说明](win11-24x7-stability-remediation-01.md)。

## 参数与分支证据入口

当前参数流程见[首配与保存](parameter-guidance-startup-01.md)和[参数清单](parameter-inheritance-02.md)。取格口默认1000ms；自动识读独立1000ms，不按RCS期限丢弃合法输入。参数回归使用`sm-parameters`、`rcs-timeout-errors`、`parameter-refactor`（已含inheritance）和`parameter-guidance-startup`，Windows增加首配默认与统一偏好部分保存验证。过期“换码即新件”、无正式候选必须产生UI输入和队列设置可编辑等预期改为当前轮次/处理回执契约。

原`industrial-coverage.runsettings`和`verify_product_coverage.py`仍只负责四程序集的实际子进程行覆盖门槛。`scripts/measure-product-branches.ps1`在独立二进制副本上使用固定Coverlet 10.1.0采集真实分支，保留OpenCover与完整JSON（[收集器源码](https://github.com/coverlet-coverage/coverlet/blob/v10.1.0/src/coverlet.core/Reporters/OpenCoverReporter.cs)的OpenCover明细会省略没有顺序行的方法，因此按配套JSON保留全部分支并核对总数），输出原报告、运行命令/退出码、源身份及`branches.json/md`。所有自有`src/**/*.cs`纳入源清单；缺采集的分支数为未知，未命中的分支逐项列明，不假定不可达，不缩小分母宣称全覆盖。不同程序集字节版本分别记账；纯WPF测试程序集中的链接生产代码不伪装成生产程序集覆盖。完整可达分支覆盖仍须以实际报告和缺口为准，硬件/权限/现场未运行不能签合格。

原生分支采集逐场景保留 `WINDOWS-WPF.log`、实际DPI和PNG，不用收集器退出码替代UI断言。Windows构建工具在失败或可捕获取消后保留Job所有权，显式终止并确认 `ActiveProcesses == 0` 后才关闭句柄；确认超时或Win32失败明确失败，不报告已排空。取消测试使用独立进程组的可捕获控制事件并保留返回后子进程停止断言；[Python的Windows SIGTERM](https://docs.python.org/3.12/library/subprocess.html#subprocess.Popen.send_signal)是直接终止，不能当作finally已执行。强制终止仍依靠原kill-on-close保护，不把父进程退出当作同步排空证明。Job终止与活动计数分别按[TerminateJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-terminatejobobject)和[Windows记账契约](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information)核对。

## 窗口行为验证边界

按钮/弹窗整改沿真实入口验证任务导航、原账读取失败/重读、连续采样停止与完成/关闭竞态、到期授权和结束失败、偏好部分保存及完整备份回执。动作以明确身份表达，不以中文或控件顺序锁定测试；删除失效入口的专属断言，保留原权限、资源归属、请求防重与恢复断言。原生截图绑定来源、尺寸和实际DPI，软件/合成回归不替代真实SDK、客户RCS或现场资格。

`camera-rounds`经两种识码路线的实际采图实现和诊断存储核对连续试扫来源：只注册所选相机、另一相机仅预览、停止不清空未选相机缓冲、换位重开保持原样本且恢复供包。`debug-batch-actions`核对WPF冻结同一相机身份及停止/失败重试；其受控分页只用于竞态，不能单独证明真实采集。`first-preference-save`串联业务提交回复丢失、本机偏好失败和维护释放失败，核对原服务保留、一次业务CAS及最终恢复。`sm-dispatch-resources`另核对缺省1000ms下的真实发送许可等待与结束未尝试轮次；完整HTTP拥塞和期限临界仍使用各自既有场景，不以本地许可冒充远端接收。
