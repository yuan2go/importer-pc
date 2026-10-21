# PR73 CI 与审查跟进

2026-10-21。用户授权持续修复并验证至具备合并条件。原 PR73 已于2026-10-20合并为 de2b045，原发布阶段不测试授权与历史失败不倒写；本次基于 main dfec787，另建 codex/pr73-startup-recovery 后续PR。并行PR78参数V3未合并，不修改其分支、表单或保存流程。

## 当前裁决

修复原PR73未解决P1：首配判断只能依据是否存在可读取的配置权威，不能因Pending静态校验失败而阻止Runtime走原有Effective回退。没有配置账但有可编辑种子/新站候选时进入既有首配；损坏或不可读取的原账进入普通修复并保留原件。已有配置账直接进入原Runtime受控启动、激活和回退，失败证据和Pending继续保留；不自动应用、不重发原请求、不改变权限/设备所有权。

## CI基线

原PR73最后实际运行的5fca596检查：Windows checkout和portable/win-x64冻结构建已通过；原生冻结旅程工程门禁失败（39406803035 / 118574891319），不能记成PASS。已读取原始artifact 12256758546：core/media/removal通过，WPF夹具编译缺少VerifyParameterRefactor而失败；并非生产业务运行失败。合并后main已含PR74对完整同源WPF夹具及相关导航/字段断言的修复，按当前脚本重新运行，不重复建立夹具或削弱门禁。

## 实施与验证

已先完成路由裁决与相关文档，再修改代码。DesktopLaunchOptions的内部路由使用原StartupMaintenanceDocument事实，不新增配置权威；App不再在正常启动前Check Pending。没有权威账的可编辑候选进入首配，不可读取原账进入普通修复，已有权威交还Runtime。

Application.Verification和Sportmaster/DirectPlc/WcsToPlc桌面Release构建全部0警告/0错误；[启动确认40项](evidence/pr73-review-followup-01/configuration-confirmation.log)通过，含新增的真实持久配置/原请求回退、资源创建范围、缺参默认及损坏原账保护。依赖失效使用明确标注的合成工厂，未冒充真实SDK/包移除验证。

最新main新增V2原型也含Windows保留名aux.js；两个Windows工作流沿已有稀疏checkout规则增加这一精确路径，仅省略工作副本落地。原设计Git blob、冻结完整导出和签名路径安全守卫均保持。后续PR的实际远端结果见下节。V3设计、PR74/76发布和D/RV原证据各保留来源。本次不操作真实设备或发布生产介质。


## 实际远端闭环

[后续PR79](https://github.com/yuan2go/importer-pc/pull/79) 的 `5d722f8a4eebaa3a7d034c9c6becbfe8f63f645b` 已完成 [Windows frozen-sportmaster CI](https://github.com/yuan2go/importer-pc/actions/runs/39465324737/job/118775490050)，结论success；测试的是GitHub合并树 `5edfe080157d415679a4798918589f64d4dcfe41`。双目标冻结构建、core、WPF原生旅程26条PASS、同版视觉119条PASS、removal及engineering门禁全部通过。production门禁按预期FAIL，保留真实安装/设备/现场未验项；这不是生产验收。

已下载artifact 12284432859并核对其完整ZIP SHA256 `00ddae3b8d4cedd9e3ffdc5cd786bc4415342d33c3a0953709f00708345f811d`，各RESULT和门禁回执均来自上述合并树，见[原回执身份摘要](evidence/pr73-review-followup-01/remote-ci-result.json)。原始完整artifact保留在GitHub；不把原PR73失败改记为通过。唯一CI warning为上游Action的Node20弃用提示，runner已用Node24执行，不影响本次成功结论，本包不扩张升级Actions。

[自动代码审查](https://github.com/yuan2go/importer-pc/pull/79#issuecomment-6296838376)对核心修复 `2fcc1ca` 已完成，has_any_findings=false / has_unresolved_findings=false；之后仅修改启动进度文字，避免在运行层实际校验前声称参数已校验。本次没有自行提交批准审查或绕过保护。原PR73的P1线程 `PRRT_kwDOVLQU8s6uSbPM` 已在修复及CI通过后解决；修复承载于PR79，合并前main仍保持原版本。

本回执提交只更新文档和manifest，不修改已验证运行源码。最终可合并状态继续以PR79最新提交的Checks、审查及GitHub冲突状态为准；不在构建中宣告完成，不执行未经请求的合并。并行PR78及原发行介质均未修改。
