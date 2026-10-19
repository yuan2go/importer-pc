# Sportmaster 商业交付风险验收

关联 [设计](../../design/sportmaster-commercial-delivery-01.md)、[工作包](../../work-packages/WP-IPC-SPORTMASTER-COMMERCIAL-DELIVERY-01.md)。初始状态全部 NOT_RUN；真实依赖 BLOCKED。最终证据见本包结果，不倒写此计划为历史执行。

|必需项|范围与断言|计划环境|
|---|---|---|
|core|原 COMM 夹具：未确认配置失败修复、CAS、固定请求/Unknown、完整备份与支持预览|冻结实际 DLL，合成端口|
|wpf|首配分步/输入保留/任务动作/检查失效、帮助搜索、共享 Application|冻结实际 Windows DLL；不等于现场 DPI/触控|
|media|双目标库存、ZIP、源码、业务/ABI/DB 和共享 DLL；门禁拒绝伪证据|同一发行器/冻结源|
|removal|卸载白名单、原数据保留、真实文件占用及中断内核重入|实际 Windows DLL，合成重启登记，不修改系统安装|
|windows-install|指定账户、非默认目录、helper/ACL/登记/快捷方式、正常启动|一次性 Windows VM；UAC/第二账户|
|windows-repair|首次失败修复、未保存保护、系统登记修复、程序并排升级、错误介质拒绝|同一冻结介质；原责任不清必须拒绝|
|windows-uninstall|控制面板入口、保留数据卸载、占用重启、中断继续|可重启隔离 VM；不操作共享宿主机|
|windows-migration|完整备份/换根/错误 SID/换机秘密/源退役/恢复隔离|两账户/两环境；旧请求不重发|
|field|真实 SDK/合同/稳定身份/实体许可/断网连续班次/恢复与现场 UI|缺证据 BLOCKED，不能以仿真替代|
|signoff|分发签名、资格及负责方生产放行|缺证书/批准 BLOCKED|

仅执行本包相关 Python 门禁测试、必要构建、上述局部夹具和冻结发布启动检查；不重复其他 Application 的无关全量测试。测试失败保留日志并修复，不删断言；最终逐项结果引用确切 SHA/介质和环境。
