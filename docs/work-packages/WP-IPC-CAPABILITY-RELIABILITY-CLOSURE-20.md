# WP-IPC-CAPABILITY-RELIABILITY-CLOSURE-20

用户2026-10-18授权A～E及可靠性/交付要求为完整范围；执行基线08286542bbd007433d85be2cb8d129b366b007e0。原要求在会话保留，本映射不缩减目标。隔离工作树importer-pc-capability-20；主目录未跟踪资料不触碰。PR19已合并；并行PR20备份/健康恢复不纳入本包。

## 实施计划（writing-plans，本会话顺序执行）

- [x] A：ParcelRuntime/Guards/Images/Stages统一主链、重量、采集和上传独立守卫；实际Application入口验证Attached未知/拒绝×重量、超时和终结回调。
- [x] B：ParcelImages/Commands/Stages无图安全取消释放预留，持久后删除，重启一致；未知原图保留。
- [x] C：ParcelViewModel/ShellWindow普通写互斥与Pause/Refresh分离，CapabilityEditing读码目标只读且重验；package-application交付protect-release.ps1及操作指南。用链接实际ViewModel源码的跨平台验证入口，不模拟另一个ViewModel。
- [x] D：ParcelCapabilityFactory参考规则局部化、组件schema及真实身份；复用受信store/精确加载，以版本化资格证据及实际前置文件校验Production。缺真实协议只标对应BLOCKED。
- [x] E：配置/原件策略明确版本，过期未发原意图可处置且保留原事实；三个Application完成条件与组件声明相容，拒绝不能完成配置。
- [x] V：新增--reliability-closure少量定向案例，复用19/18和资源恢复必要断言；必要构建、受影响Windows候选交叉构建/解压检查。Windows实际NOT_RUN。
- [ ] 交付：设计、运行、能力矩阵、manifest和回执；冻结源码提交，候选全新目录和版本；推送开发分支创建main PR，不合并。

验证入口：dotnet build tests/Ipc.Application.Verification -m:1 -nr:false；相同程序集运行 . --reliability-closure 与 . --capability-closure。新增测试先记录红灯，再修复并保存结果，不删除旧断言。源码/配置/指南提交后用scripts/package-application.py打包并解压检查；不复用旧包。

源码完成后冻结提交；冻结身份、实际验证及最终PR见[实施回执](../runtime/capability-reliability-20-results.md)。新候选只核受影响闭包，不重复六候选/18全组合；Windows实际仍NOT_RUN。

最终运行源码a147eac7e4bafb70e16b2c677c2a2933909234d3；A～E、44条定向断言、17/19/平台基线、实际签名链和四个新候选解包/四条进程冒烟完成。Windows实际NOT_RUN，真实能力按所选证据BLOCKED。PR交付回执在实施报告补录。
