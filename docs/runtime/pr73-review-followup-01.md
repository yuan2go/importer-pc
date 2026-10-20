# PR73 CI 与审查跟进

2026-10-21。用户授权持续修复并验证至具备合并条件。原 PR73 已于2026-10-20合并为 de2b045，原发布阶段不测试授权与历史失败不倒写；本次基于 main dfec787，另建 codex/pr73-startup-recovery 后续PR。并行PR78参数V3未合并，不修改其分支、表单或保存流程。

## 当前裁决

修复原PR73未解决P1：首配判断只能依据是否存在可读取的配置权威，不能因Pending静态校验失败而阻止Runtime走原有Effective回退。没有配置账但有可编辑种子/新站候选时进入既有首配；损坏或不可读取的原账进入普通修复并保留原件。已有配置账直接进入原Runtime受控启动、激活和回退，失败证据和Pending继续保留；不自动应用、不重发原请求、不改变权限/设备所有权。

## CI基线

原PR73最后实际运行的5fca596检查：Windows checkout和portable/win-x64冻结构建已通过；原生冻结旅程工程门禁失败（39406803035 / 118574891319），不能记成PASS。已读取原始artifact 12256758546：core/media/removal通过，WPF夹具编译缺少VerifyParameterRefactor而失败；并非生产业务运行失败。合并后main已含PR74对完整同源WPF夹具及相关导航/字段断言的修复，按当前脚本重新运行，不重复建立夹具或削弱门禁。

## 实施与验证

已先完成路由裁决与相关文档，再修改代码。DesktopLaunchOptions的内部路由使用原StartupMaintenanceDocument事实，不新增配置权威；App不再在正常启动前Check Pending。没有权威账的可编辑候选进入首配，不可读取原账进入普通修复，已有权威交还Runtime。

Application.Verification和Sportmaster/DirectPlc/WcsToPlc桌面Release构建全部0警告/0错误；[启动确认40项](evidence/pr73-review-followup-01/configuration-confirmation.log)通过，含新增的真实持久配置/原请求回退、资源创建范围、缺参默认及损坏原账保护。依赖失效使用明确标注的合成工厂，未冒充真实SDK/包移除验证。

最新main新增V2原型也含Windows保留名aux.js；两个Windows工作流沿已有稀疏checkout规则增加这一精确路径，仅省略工作副本落地。原设计Git blob、冻结完整导出和签名路径安全守卫均保持。后续PR远端CI与审查仍在跟进，不提前声明PASS。V3设计、PR74/76发布和D/RV原证据各保留来源。本次不操作真实设备或发布生产介质。
