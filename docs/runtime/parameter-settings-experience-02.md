# PARAM-UX-02 · 参数设置 V3 实施与运行

> 2026-10-21当前状态：已实际核对main `42a047c3c0dbba3208e6bc6ce2910cd6ffabbf5e`，PR78参数V3及PR79启动回退均已合并。V3原设计54个文件完整、53个登记文件哈希及25个源码文件指纹全部匹配。当前剩余工作与精简执行提示见[原工作包的收口更新](../work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)。以下形成时基线、原交叉构建和Windows未验记录保留，不扩张为本次或最终main验收。

用户要求重新设计实际参数页并完成优化。本轮先查看1708×960原截图、读取main `837370e`的代码/文档、研究Rider/PowerToys/Visual Studio设置，再建立V3当前指导后改代码。完成阶段三方整合main `bf61337`的PR76/77，保留操作员会话时长修复与1.6.1原发布身份。

## 成果与当前状态

| 范围 | 实际交付 |
|---|---|
| 设计 | [V3图集与原型](../ui/parameter-settings-v3-20261020/README.md)：27张高清PNG、可编辑离线原型、49条字段/聚合映射、状态/按钮/任务交接及简洁Codex提示 |
| 原生源码 | 同窗专注参数工作区、单目录四类对象、中文表单/宽度/高级信息、统一保存审阅、具体工具入口、搜索定位、原始绑定错误保护 |
| 契约 | 复用同一Application、参数模型、原保存账和Stage/Inspect；审阅按当前编辑来源，差异不漏未知技术路径；高级JSON采用不改变已保存基线 |
| 主线整合 | 继承PR76日常实际登录角色、首次配置Bound、解绑保值、错误仍可达、维护认证独立及原测试，只适配新目录/搜索方式 |
| 本轮验证 | 10组原型PASS、27图实际走查、字段/保存静态复核、锁定还原及Sportmaster UiVerification交叉构建PASS；[具体证据](../ui/parameter-settings-v3-20261020/QA.md) |
| 待验 | Windows原生执行、目标工控机/DPI、真实驱动/RCS与现场均未执行；没有新安装包发布，原1.6.1不会因入库设计自动包含V3 |

设计已按授权进入main；业务源码原由 [`codex/parameter-settings-v3-20261020`](https://github.com/yuan2go/importer-pc/tree/codex/parameter-settings-v3-20261020) 创建PR78，并已合并为`2986733087c9cf2602658cab6c8629773fefdcda`；PR79随后合并为上述`42a047c3`，见[发布记录](publication-pr78-pr79-20261021.md)。原源码文件身份见[SOURCE-STATE](../ui/parameter-settings-v3-20261020/SOURCE-STATE.json)，不改写为新一次Windows验证。

## 主要实现位置

- `ParameterSettingsControl.*` / `ParameterSettingsTheme.xaml`：单目录、类别记忆、真实字段定位、按实际槽位的输入宽度及状态样式。
- `MaintenancePage.xaml/.cs`：底部统一保存、自动校验、具体工具及模板预览；文件选择/ShowDialog不占用整个异步工作通道。
- `ConfigurationReviewPresentation.cs` / `ConfigurationSaveReviewWindow.cs`：真实差异、敏感信息保护、当前审阅签名、按需授权及原结果核对。
- `ConfigurationSubmission.cs` / `StructuredConfiguration.cs` / `ConfigurationCloseGuard.cs`：保留原CAS、提交冻结和有效输入来源；转换错误不被旧VM值掩盖；JSON转表单不误认已保存。
- `SupplyStationWindow.*` / `SupplyStationViewModel.cs`及共享Shell：参数模式隐藏重复日常侧栏，保留同窗实例、500ms真实刷新、独立暂停、左右摘要和视口保护。
- `Ipc.Desktop.UiVerification`：仅适配受影响导航、保存审阅和会话搜索断言；保留原权限、CAS、Pending、错误原文和关闭保护。

## 当前参数操作

打开参数设置 → 左目录选择系统/业务/驱动实例/组件 → 输入 → 点击“保存更改…”自动校验。错误直接定位，其他输入保留；通过后一次审阅全部变更、暂停/原维护条件及原因，确认后仍沿原Stage/Inspect。打开审阅不要求已具备CanConfigure；最终提交必须满足。

当前CanConfigure由暂停、维护授权、无在途外呼等原Application资格给出，不把false错误解释为“必须重新登录”。原配置Revision和RuntimeId不会因快照刷新推进。待确认回执只核对原次提交；保存待重启与运行中分开，重连不应用Pending。普通返回作业保留草稿且不自动恢复入场。

“维护与迁移”已从日常表单撤除。必要能力改从自动备份、设置模板、加密未完成编辑、安装/角色绑定、运行配置/原账核对、原机恢复后核对等具体任务进入；完整备份仍含数据库与同名`.recovery`，安全模板仍只含原白名单。

## 不扩张为生产能力

原型的连接检查是明确标注的离线模拟。原生本轮没有新增设备抢占Probe或RCS业务试单；现有无权威Query限制继续表达。UI不能给出未经证实的连接成功、业务接受或运行配置生效。

[V2实施](ui-task-redesign-01.md)、PR74/76介质与历史Windows结果保留原身份。后续按[PS工作包及其最新Codex执行提示](../work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)收口实际Windows工作，不重做已正确业务，也不增加不必要测试。设计包内原CODEX-NEXT保持原哈希，形成时分支语境由当前工作包说明。
