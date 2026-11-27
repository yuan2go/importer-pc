# Stage 2 测试清洗与重组报告

基线为 Stage 1 提交 3ac73fbd。两份工作树均使用 D 盘项目环境、锁定依赖和 Sportmaster 组合。审计表列出十个测试工程、原调度目标和调用方。[测试资产审计表](stage-2-test-asset-audit.md)

## 资产变化

| 口径 | Stage 1 | Stage 2 | 差值 |
|---|---:|---:|---:|
| tests 跟踪的 C# 文件 | 268 | 266 | -2 |
| tests C# 源码行（含注释与空行） | 40,552 | 38,920 | -1,632 |
| Application.Verification C# 文件 | 202 | 196 | -6 |
| Application.Verification C# 源码行 | 30,915 | 29,245 | -1,670 |
| Program 调度入口（含首参数助手和同分支别名） | 197 | 165 | -32 |
| Program 唯一参数字面值（含选项） | 202 | 170 | -32 |
| Core 进程套件 DataRow | 75 | 69 | -6 |

退役 45 个原调度名，增加 13 个领域入口，净减 32 个。原有 55 个无直接调用线索的入口中包含子进程、离线回放和工作流动态参数，审计表记录了逐类处置理由。

## 回归分组

| MSTest 类别 | 进程套件 | 范围 |
|---|---:|---|
| [Platform](../../../tests/Ipc.Core.Tests/PlatformRegression.cs) | 15 | 存储、备份、恢复、配置、日志与运行身份 |
| [Deployment](../../../tests/Ipc.Core.Tests/DeploymentRegression.cs) | 14 | 安装、回执、维护、回滚与卸载；机器级操作串行 |
| [Drivers](../../../tests/Ipc.Core.Tests/DriversRegression.cs) | 12 | 设备采集、识码、在位模型、SDK 断线与释放 |
| [Sportmaster](../../../tests/Ipc.Core.Tests/SportmasterRegression.cs) | 28 | 供包轮次、双位调度、RCS、业务设置、调试与展示 |

69 个进程套件由 MSTest 分别记账，另有 26 项独立 MSTest 用例。软件、原生采集的换车场景各自有看门狗，各保留 30 个耗时样本。

共享 SQLite 格式测试从已注册的 DirectPlc 业务取得数据所有者，覆盖 WAL、同步级别、损坏拒写和原件保留；独立的 SM 启动冒烟继续使用 SM 配置。备份删除测试使用两个合法业务身份核验归属。

## 编号套件归属

| 原入口（省略双短横线） | 当前套件 | 保留的行为 |
|---|---|---|
| cr-sm-removal、same-placement-29 | Sportmaster / sm-round-lifecycle | 同车换件、原轮次人工移出令牌、展示和提交分离 |
| cr-sm-dispatch | Sportmaster / sm-dispatch-resources | 两工位并发、在途请求与资源释放 |
| cr-sm-live-devices、live-devices-30 | Sportmaster / sm-camera-reconfiguration | 交换指定设备，保留未释放所有者，另一位继续工作 |
| cr-sm-backup-ownership、cr-sm-backup-fences | Platform / backup-set-delivery | 异业务文件归属、删除回执、准备失败后撤销冻结 |
| first-configuration-03、cr-sm-templates、cr-sm-preferences | Sportmaster / sm-configuration-preferences | 首配、设置编辑身份、模板和 Windows 用户偏好独立保存 |
| cr-report-boundaries | Platform / platform-custody-isolation、parameter-refactor，Deployment / installation-receipts，Sportmaster / sm-configuration-preferences | 原始回执、授权、CAS、收据顺序与编辑身份分别走实际持久化或发布路径 |
| cr-report-cameras | Drivers / camera-acquisition-boundaries，Sportmaster / sm-arrivals | SDK 借帧、故障收尾、心跳确认丢失与同次到位恢复 |
| reader-feedback-22、reader-robustness-28 | Drivers / camera-acquisition | 驱动到应用的帧反馈、候选过滤、原图与识读质量 |
| debug-no-isolation-31 | Sportmaster / sm-debug-requests | 调试授权、明确发送和原始回包 |
| installation-receipts-04、permanent-deletion-02 | Deployment / installation-receipts、permanent-data-deletion | 安装收据摘要及永久删除范围 |

## 五组安全不变量

| 不变量 | 可复查的回归入口 |
|---|---|
| 存储与事务 | storage-schema-only 检查 DB14 格式，backup-consistency 和 backup-set-delivery 在真实 SQLite、回执文件和备份集合上验证。lifecycle-restore 和 platform-custody-isolation 检查隔离恢复、归档及保留期输入归属。storage-baseline 保留 SQLite WAL 检查，win11-stability 及工作流另含保留期专项。 |
| 轮次与防重 | sm-round-lifecycle、sm-dispatch-resources、sm-arrivals 覆盖一次提交、换车、Unknown 与发送围栏；mvs-presence 分别验证 300 ms 移出、800 ms 校准以及超 500 ms 断帧；sm-single-lane-calibration 核验空闲单工位操作。 |
| 采集与通信 | mvid-reader、mvs-still-frames、camera-acquisition-boundaries 验证放稳前的解码和 SDK 资源，sm-camera-reconfiguration 核验发现与身份。rcs-timeout-errors 使用精确期限用例，rcs-heartbeat 保留紧凑探测。 |
| 配置与权限 | parameter-refactor、sm-parameters、platform-custody-isolation 验证 CAS、字段归属和授权；runtime-identity-boundary、maintenance-launch-fence、Operations 验证运行身份、授权回收和双进程命名管道。 |
| 安装与生命周期 | InstallationIsolation、InstallationResourceIsolation 区分安装身份及机器资源。shortcut-lifecycle、uninstall-assets、industrial-rollback、permanent-data-deletion 覆盖受控快捷方式、自启、卸载和回滚。 |

## 调度与计时

MSTest 暴露 Platform、Deployment、Drivers、Sportmaster 四类，保留 Core 类别与安装隔离过滤器。隔离套件最多同时运行三个子进程，每个进程独占临时目录和模拟用户根，成功后清理本次目录。部署组和机器资源隔离组串行。子进程退出码、至少一条实际行为断言、FAIL 输出及执行期限均继续检查。

备份复制回调通过进入信号与释放屏障协调，提前结束时返回原备份结果。合成相机确认帧号、驱动完成及应用队列状态，换车依据对应心跳的真实响应。恢复启用等待维护探测释放后的实际准入。请求精确期限和像素模型的时间阈值分别使用已有可控时钟与合成帧时间戳。等待监控线程实际写出队列采样再释放阻塞；软件与原生换车各自保留 30 次实测，并独立执行看门狗。原生 SDK/网络耗时测量保留真实时钟。

三份工作流中的应用验证调用已按实际可执行程序核对。海康工作流的排版检查位于独立 WPF 作业；驱动作业曾把同一参数传给应用验证器，已移除这次错误调用。商用流程保留实际使用的旧名子进程；PowerShell 识码验证改调合并后的套件。

当前代码仍有用于 SDK 耗时分段、模拟阻塞传输、子进程存活与有限资源轮询的真实等待。耗时测量保留原始数据；MVS 模型阈值、RCS 总预算、一次提交及备份所有权均有独立行为断言。双路 TCP 在空闲工位验证暂停；失败启动通过 SQLite 原输入核对停机跳过。同位复读、凭据脱敏、旧快照丢弃及重启后的到位确认按现行契约验证。管理窗口读码检查核对原生产作业与持久化输入，并验证仍显示的卡片指向同一输入，允许正常展示到期。

## 验证结果

| 执行 | 通过 / 失败 / 跳过 | 命令墙钟（含构建） |
|---|---:|---:|
| Stage 1 原版 | 99 / 2 / 0 | 24 分 42.4 秒 |
| 最终全量第 1 次 | 95 / 0 / 0 | 9 分 32.7 秒 |
| 最终全量第 2 次 | 95 / 0 / 0 | 8 分 55.1 秒 |

Stage 1 原版有两项失败，分别发生在相机依赖文件替换和导出完成等待。基线后半段与探索性运行在同一机器上重叠，耗时为本机实测记录。

核对范围包括 69 条领域 DataRow 和工作流中 46 处直接或展开后的应用验证参数。逐项搜索 26 个已移动或删除的测试文件名，活跃工作流、脚本、测试和文档均无失效路径。按完整参数在活动脚本、源码、测试和文档中搜索，已退役入口无残留引用。历史审计保留原名。


两次回归的平均墙钟耗时为 9.23 分钟，比本机 Stage 1 记录减少 62.6%。相对任务给出的 18 分钟参考值，平均缩短 48.7%。耗时含还原检查、构建和运行；机器休眠打断的探索性运行未计入。

| 本机门禁 | 最终结果 |
|---|---|
| Sportmaster 桌面构建，锁定依赖，单节点 | 通过，0 警告、0 错误 |
| Ipc.Operations.Verification | 通过。验证独立 WPF 进程、IPC 身份与正常退出释放所有权 |
| Ipc.Desktop.UiVerification --typography | 通过，120 DPI 环境实际解析和渲染字体 |
| test_industrial_ci.py | 4 项通过 |
| verify-industrial-docs.py / verify-industrial-format.ps1 | 通过，71 个索引文件、323 个本地链接，源码空白格式通过 |
| TestInstallation=true，独立产物目录 | 安装身份与资源隔离 11 项通过，0 失败、0 跳过 |
| 工作流专项 | dual-camera-communication、hikrobot-sdk-contracts、sm-snapshot-ordering、sm-rcs-request-order 均以 Release 构建实际运行通过 |
| storage-baseline | 使用另一已注册模块的所有者验证共享格式，再运行 SM 模拟启动；通过 |
| 远端 GitHub Actions / 真实相机与 RCS 硬件 / 客户联调 | 均未运行 |

单独探查的 review-regressions 需要受保护的资格证据目录。本机通过了包签名检查，在目录所有者核验处被拒绝。审计表保留了该专项的调用方式与前置条件。

## 复现核心回归

在仓库根目录运行以下命令。核心适配器同时检查行为子进程的退出码与断言输出，命令返回后可从 TRX 的 Counters 核对通过、失败与跳过数。

~~~powershell
. ./scripts/project-environment.ps1
dotnet test tests/Ipc.Core.Tests -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false --logger trx
~~~
