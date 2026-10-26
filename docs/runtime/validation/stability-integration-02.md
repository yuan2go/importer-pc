# 稳定性与生命周期分支整合

状态IMPLEMENTING，尚无集成PASS。范围见[工作包](../../work-packages/WP-IPC-STABILITY-INTEGRATION-02.md)。原PR108/110/111/112及其原证据继续独立；本页记录补缺和最终集成源码/验证，不改写旧FAIL。

PR110快捷方式固定源980a1383三组ACP与安装守卫本机复验通过；当前父子分支255ccd4a已包含它。PR112原任务仍在补同源验证，未接管其工作区。恢复全位暂停的假设待新增负向断言确认。历史ST08“未实现/等待依赖”只属于原ec808阶段，当前事务/致命通知/监督退出已存在；完整集成资格待本页。

[编码前原件](../../archive/pre-stability-integration-02/FILES.json)。

## 恢复暂停补缺：先失败

原Unknown/已持久输入三场景新增全位暂停断言通过，原实现正确，不重复改写。新增首次输入在提交前持续故障、没有既有未决作业/输入的budget-empty场景，有限重启后Paused=false，仍有位可准入，新增断言明确失败。首次把断言放子进程触发原致命钩子、造成父预算取消的尝试保留原FAIL；最终在子进程落观察、父进程断言，才用于根因。见[原身份及日志hash](../evidence/stability-integration-02/recovery-pause-red.json)。

修复采用既有BusinessLaunch的恢复启动上下文，桌面所有监督子进程启动向Application传递“需恢复核对”；Application在原Paused账写入暂停后才启动接收。没有新配置/新状态账、没有自动Resume。正常显式Resume继续检查权限/健康/原责任，不能清Unknown。部署正常首次监督启动也要求明确开始，不能用登录自启动获得供包授权。
