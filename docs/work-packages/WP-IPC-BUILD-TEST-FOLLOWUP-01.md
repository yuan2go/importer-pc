# WP-IPC-BUILD-TEST-FOLLOWUP-01

起点：main `52c7e58d87a5bfe452e77f22b543e1ee0cb124dd`。用户授权继续修复上一包记录的三个构建/测试问题，保留Win10 22H2＋Win11，不作纯命名重构。使用新的D盘专用worktree；本机只检查源码、编译及纯策略，不运行产品、安装器或读取既有运行目录。实际运行、恢复及脚本平台矩阵交隔离CI；历史证据、Releases、安装和数据不改。

|问题|核实与处理|验证|
|---|---|---|
|build.sh默认组合冲突|脚本封装的是Supply，原裸Solution却混入默认Sportmaster安装器。现在明确构建Product、Fixture、Supply验证和Supply桌面，以SelfContained=false统一源码编译，随后封装实际输出。正式安装器沿原发布入口构建。|CI实际执行完整build.sh，检查两份输出均为Supply|
|无参数Application验证过期断言|DirectPlc/WcsToPlc第一件仅PLC接受时，物理位置仍被占用。先断言第二实体被阻断，再显式查询原请求完成事实，随后准入第二实体；第二件保留未完成状态以继续验证隔离恢复查询。|CI实际无参数调用，要求完整AC17完成标记|
|station默认路径与平台|默认数据根改为64位HKLM存储Root/UserData/当前SID/data，缺登记拒绝并提示安装配置或显式参数；Fixture使用所选根下独立SimulationFixture。平台与GUI相同：Windows10内核14393起、原生x64 OS/进程，Win10 22H2与Win11为优先目标。PowerShell7显式必需。|纯策略矩阵不读注册表、不启动产品；现有C#客户端平台测试同步执行|

路径解析不取代应用对存储登记、ACL、祖先路径、固定账户及租约的校验。默认根按本项目要求在安装时选择D盘，显式DataRoot继续可选。没有自动完成、自动重发、假查询或降低持久化。此包无数据库/公共契约变化，不提升版本；当前文档与规范manifest同步。

默认套件同步补齐原请求查询的管理授权；观察验证等待实际Duplicate/Blocked记录，原始回包恢复验证等待AfterRawDurable故障边界后再检查工作终止，避免未开始工作就判定完成。本机纯策略19项、随包文档3项通过；Application验证工程Release/locked构建通过。实际运行只在隔离CI执行。

提交、PR、CI及合并结果以本包PR和最终交付记录为准；不从旧提交转记PASS。Win10/Win11目标机、真实SDK/RCS和现场为NOT_RUN。回滚采用正常反向提交PR，不重写历史。

首轮CI（40468133005）的Linux桌面跨编译在System.Diagnostics.EventLog引用包还原时NU1102失败，核心、Fixture和Supply验证已成功编译。完整Supply桌面构建改在Windows runner实际执行Git Bash入口；无参数默认套件继续独立在Linux隔离运行。没有修改NuGet锁、包版本或跳过桌面构建；Linux桌面跨编译不计PASS。

第二轮无参数套件已通过三业务作业、PLC位置阻断/完成、恢复与称重，暴露Unknown目标比较混用了操作员脱敏投影和管理完整投影。原件改为授权后捕获，重启也在授权后比较；保留ActivationFailed及原请求字节/次数断言，并增加Pending候选保留断言。
