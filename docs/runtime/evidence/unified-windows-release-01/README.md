# 统一发行证据

二进制固定7acbb2c；RELEASE/BUILD记录实际SDK、源码、命令、日志和续建编排脚本hash，SHA256SUMS校验8个ZIP与RELEASE。所有包为本任务新构建，不借用PR47或旧Release。

- 三个`*-smoke.json`及events由`scripts/smoke-application.py --package <portable解压目录> --dotnet <dotnet> --output <新JSON>`实际执行，都是参考模拟，非外部设备协议验证。
- supply/inspection-smoke记录实际发布Assembly启动、snapshot、quit的完整命令/输出。legacy-versions来自各自实际deps.json，不能按Parcel版本或公共DLLhash混装。
- FrozenProbe.cs直接引用冻结Sportmaster portable app下的DLL，目标net10.0、ImplicitUsings enable，入口参数为portable解压目录及可选win-x64解压目录。探针使用临时合成数据及显式合成维护授权，不构造业务设备；frozen-media-probe为实际Windows媒介由冻结Platform检查。七种账户判断不是Windows令牌试验。
- extension-build-3072为3072位TestOnly签名包的实际静态接纳；extension-build.log保留首次2048位输入被最小强度规则拒绝，未放宽门禁。extension-negative-checks保存篡改/Production拒绝和跨目标合同hash；不加载模块。测试私钥不保存或上传，测试包不随默认产品介质部署。
- static-entry-order为三个原源码顺序断言；未执行需要PowerShell的整套测试。resume-lock-check为实际内核锁并发拒绝且原BUILD不变。
- first-failed与cancelled保存原本地尝试，不计入成功发行。attempt-*为环境中断前原BUILD，interrupted-*为原中断日志；原件身份不覆盖。

后验仍按`docs/runtime/validation/startup-deployment-02-windows.md`执行。无Windows UAC/ACL/不同真实账户/中断/升级恢复/桌面操作结果；真实SDK/设备/生产资格BLOCKED，商用NOT_APPROVED。手动Actions工作流本轮NOT_RUN。

并发整合后：当前活动发行机制来自aaa30b5及其后继，不以本档案覆盖。orchestrator-used-for-7acbb2c.py为历史实际编排源码（hash匹配RELEASE），供追溯，不是当前运行入口；不要从证据目录直接执行。
