# 当前运行身份预检

Supply ProductRuntime、InspectionRuntime和ParcelRuntime的StartAsync在配置读取、扩展访问、持久资源和设备构造之前调用MachineRuntimePolicy.RequireRuntimeIdentity。普通运行身份、独立安装/维护权限分别验证，不以日常运行门禁替代维护权限。

Supply/Inspection在Ipc.Verification的--runtime-identity入口执行共享SID/claims判定及真实StartAsync拒绝行为检查；Parcel三业务在Ipc.Application.Verification的--runtime-identity-boundary入口执行。故意无效的原配置必须在身份拒绝后原字节不变，无新数据根、租约、账或设备。Supply保留NotReady诊断语义，Inspection/Parcel保留异常语义。仅friend验证程序集可注入Supply/Inspection拒绝委托，生产默认仍是机级策略，不新增运行配置开关。

Python test_runtime_identity.py验证真实原生子进程分派、登记根、拒绝后的原件不变及独立维护入口。Supply离线维护入口的源代码结构检查暂保留：它约束独立权限和控制流，不能由普通运行拒绝测试代替。真正Windows指定账户、ACL、系统安装和提权维护须另在获授权环境验收。

本次结果见[03报告](contract-convergence-03.md)。原02设计及验证文字见[原字节归档](../archive/pre-contract-convergence-03/README.md)，原evidence/runtime-identity-preflight-02记录保持形成时身份。
