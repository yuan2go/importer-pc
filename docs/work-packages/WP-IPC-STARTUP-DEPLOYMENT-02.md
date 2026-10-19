# 启动修复、独立基础配置与安装向导 · STARTUP-DEPLOYMENT-02

2026-10-19用户在前轮静态审查后明确采用前三项整改并要求按工业软件部署最佳实践实施。main为524916b，继承本任务PR44/deceb25；开放PR45/506bf7a只补Supply/Inspection身份入口，不修改它或其他工作树。继续本任务现有开发分支并更新PR44，原候选1760200及历史证据保留。

## 授权设计与实施取舍

前轮已提出的独立维护模式、内置配置与扩展库存分离、安装向导由本次用户指令确认。使用brainstorming与writing-plans收敛；采用原生WPF向导及既有安装/配置/恢复机制，避免新增独立框架、业务库或万能安装服务。Windows正常使用不要求手建目录、输入hash或运行脚本。

1. 安装向导：从完整固定媒介校验来源和文件，选择已存在的运行账户及数据位置，可选独立可信扩展政策；摘要确认后一次UAC，部署到受保护ProgramFiles版本目录，准备机级策略和新数据目录。既有数据/配置不覆盖，程序升级并排部署；失败保留旧部署指针及诊断。完成后自动创建快捷方式并可正常启动。外部扩展信任独立提供，不生成Production或TestOnly信任。安装不启动SDK/业务，也不为新宿主复用旧签名资格。
2. 启动失败可修复：程序提供独立维护入口，不要求业务Start成功，不加载SDK或启动设备。按当前launch定位原配置与数据，查看失败、验证并编辑已有能力参数/基本策略；Windows写操作由显式提升维护helper执行。修改沿同一configuration.json的Revision/CAS保存Pending，Effective/原请求/DB不改，不把离线保存当作激活成功。原始配置字节备份和变更身份留证；损坏原账不能靠覆盖换空根解决。完整备份恢复复用ParcelRecovery的计划及隔离新目录，不覆盖来源；只有原运行实例完全Stop/Dispose后才允许重试或维护写入。
3. 可选扩展隔离：内置提供者及基本参数始终可读。扩展库存未配置/不可用以独立诊断呈现，不掩盖配置过的原提供者缺失；实际绑定的签名/精确版本/合同及Production校验继续严格失败，不把“库存可看”当“能力已就绪”。

UI已读取SS-UI-VIS-001 v1.1 README、图册、原HTML/分离CSS/JS及完整交接，查看设备和恢复PNG；新增安装/维护页沿用原tokens及44DIP操作、固定动作区、清晰错误文本，保留原工作台结构。PNG仍为设计导出；本机无法执行WPF，Windows实际UI/UAC/ACL/快捷方式/升级中断后验为NOT_RUN。真实SDK/现场BLOCKED，D/RV不倒写。

## 外部实践依据

- [Microsoft Shell安装安全](https://learn.microsoft.com/en-us/windows/win32/shell/sec-shell)：程序放ProgramFiles，用户/共享数据独立目录与权限。
- [最小化管理员运行](https://learn.microsoft.com/en-us/windows/win32/secbp/running-with-administrator-privileges)：仅安装与受保护维护请求提权，日常业务保持未提权。
- [安装回滚](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation)：本包借鉴失败保旧原则，采用并排目录和最后提交部署登记；不是MSI事务或工业标准认证声明。
- [Restart Manager应用指南](https://learn.microsoft.com/en-us/windows/win32/rstmgr/guidelines-for-applications-and-services)：先完成停止与资源交接，再启动新实例；不强杀设备线程。

必要验证：先做受影响基线，再验证可选库存缺失/损坏与真实必需绑定拒绝、离线配置CAS/原字节及DB不变/占用拒绝、安装文件完整性和失败保旧、三个业务装配/桌面交叉构建、固定portable及win-x64媒介检查与portable启动。Windows后验单列，不删除历史断言或加豁免换PASS。
