# WT024：监督责任与存储故障收口

本轮承接 INTEGRATION-20261028 的 ST08/ST14，基线为 main `e6d3d05075e9dcf45f8165178bdfc3e91d66cd46`。当前版本仍为 1.0.0；不改变数据库结构、原请求、Unknown、实际SDK或设备控制。

## 原故障与修复

原真实进程探针先终止监督器，再让原客户端以永久存储退出码78结束。基线替代监督器仍启动第二个客户端，最后将计数清零。修复后的同一探针返回77，只启动一次，并保留PID、开始时间、源码及映像hash，标记原退出结果不明。原失败日志保留，不能以PID不存在推断允许重启。

- 缺目录、缺账、非法账、读取被占用时拒绝；不通过租约或诊断重新创建零预算。有效的首次部署在登记前原子发布完整初始目录，已有目录只读验证。
- Running发布失败保留已取得的进程身份，标记不确定；不能先释放Process句柄就认为进程结束。只有持有且核验过的进程句柄观察到退出，才能沿原正常关闭/有限重启逻辑继续。
- 已知退出先写权威账，告警、诊断及stderr失败不得反过来解除阻断或重启。授权修复在同一监督租约中先核验，再修投影、重置已知预算；Running/Launching/不确定均拒绝。
- 迁移在业务源围栏之前持有部署登记锁和两根监督租约。按精确字节复制原账，登记记录其hash；目标冲突拒绝，已Blocked迁移后仍Blocked。两端无监督账且禁用时保持无账，不能吸收目标遗留账。
- 已有禁用部署继续禁用的升级、修复、卸载后重装不依赖监督目录；启用旧部署需要原账，不能用false→true推断从未启用。
- SQLite PERM(3)、READONLY(8)、AUTH(23)按永久故障处理；泛化CANTOPEN(14)不能自行证明永久损坏。提交结果不明不重做回调；永久故障也不等于事务必然未提交。[SQLite错误码定义](https://www.sqlite.org/rescode.html)。
- 配置账只在实际文件读写点通知业务所有者。种子读取、既有启动文件和目录租约失败单独记证；SDK回调、业务校验及机器身份拒绝不会仅凭异常类型被误标成永久存储故障。

## 验证入口及边界

命令沿仓库Actions：先执行 `scripts/project-environment.ps1`；全量构建使用 `dotnet build ImporterPc.slnx -c Release -p:Application=Sportmaster -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false`。

Application验证选择器：`--time-storage-supervisor-checks`、`--time-storage-atomic-checks`、`--time-storage-fault-checks`、`--time-storage-process-checks`、`--p0-supervisor`、`--lifecycle-ux`。新增监督缺账/锁定/保存失败/诊断失败/迁移对照，真实进程失去退出观察，实际只读SQLite及提交前后分类，实际启动文件故障与SDK/业务失败反例。测试保留原BUSY预算、WAL/FULL、原请求及Unknown不重放断言。

Windows受保护安装验证在隔离CI运行 `--installed-lifecycle-windows`：首次初始账、禁用升级及卸载后重装不重建账、启用旧部署缺账拒绝、实际部署写者锁与原账hash。该夹具是独立合成产品，不能当作真实业务/SDK/现场验收。

本机旧 `--architecture-quality-remediation` 在提权令牌下由既有身份门禁拒绝，尚未进入其业务断言；原失败保留，不修改本机HKLM或策略绕过。对应CI和最终计数以本PR精确head的Actions及合并台账为准。构建中的编译修正和历史失败分别保留，不转记成PASS。

物理磁盘/VHD故障、目标工控机、真实SDK/RCS、24h/72h和现场资格未验证；本任务不发布介质。不确定旧Running需要核对原进程责任，本包不提供强制清账入口。

## 本轮 CI 纠偏

PR143 初始候选 `98c2372c` 的 Linux 监督测试以非父进程句柄读取孤儿退出码，不能据此断言退出78。夹具改为由独立观察父进程持有真实客户端，监督子进程通过生产 RunAsync 的 launch seam 观察该进程；监督先被终止，客户端再真实退出78。保留实际退出码、仅一次启动、原身份与预算、拒绝清账的全部断言，不将未知退出解释成正常退出。该夹具行为不是生产进程启动方式变更。

迁移准备还提前复用既有 Installed 条件：已退役登记在取得部署锁、核对原登记后即拒绝，早于业务源围栏与目标监督账发布。隔离 Windows 回归覆盖登记与原账保持不变。

初始候选的隔离 Windows 安装生命周期43/普通令牌4通过，Linux AQA因上述夹具失败；这些结果只属于初始候选，修订候选必须重新运行CI。

## 10/29整合归宿

上述实施过程记录保持其形成时范围。WT024已由[PR143](https://github.com/yuan2go/importer-pc/pull/143)合并，实际main为`3a25d6ba3bea686b6bb7caaef919bb1dec2d4b12`，候选816bdd90的9项CI通过，测试树c58d838e；监督36/原子17/故障29/进程5及隔离Windows安装44条PASS。后续代码检查点035a125f仍保留实现。当前开发仅保障SM及必需基座，外部卷/设备/现场验证及其他业务暂停项见[整合终态](integration-20261028.md)。
