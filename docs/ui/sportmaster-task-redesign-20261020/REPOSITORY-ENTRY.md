# Sportmaster 重设计：仓库阅读与实施入口

设计交付v1 · 2026-10-20 · C#/WPF目标。**本目录是新UI的设计成果，尚未移植为生产WPF。** 原交付内容保持原字节，最新源码差异在[实施裁决](../../design/sportmaster-task-redesign-01.md)中说明；下一步按[UIR-00～06](../../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)及[精简Codex提示](../../work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)实施。

## 1. 先打开这些文件

| 产物 | 如何查看 |
|---|---|
| [Sportmaster-Prototype.html](Sportmaster-Prototype.html) | 下载后在桌面浏览器双击打开；离线可点击，45个命名场景，所有设备/服务/结果均为模拟。 |
| [Sportmaster-UI-Gallery.html](Sportmaster-UI-Gallery.html) | 下载打开，搜索及逐张看48张图；GitHub网页不会直接运行HTML。 |
| [Sportmaster-Design-Handoff.pdf](Sportmaster-Design-Handoff.pdf) | 22页完整交接；另有可编辑[Markdown](Sportmaster-Design-Handoff.md)和[HTML](Sportmaster-Design-Handoff.html)。 |
| [Sportmaster-UI-Atlas.pdf](Sportmaster-UI-Atlas.pdf) | 49页图册：封面及48张独立界面；每张高清PNG另存于screens。 |
| [Sportmaster-Field-Mapping.html](Sportmaster-Field-Mapping.html) | 可搜索58项字段/状态与39项操作，查原截图出处、归属、校验、生效及迁移。 |
| [原型源码说明](source/README.md) | 可编辑CSS/JS、场景清单、标准库重建脚本和字体许可；不是要求替换WPF技术栈。 |
| [原交付README](README.md) | 原设计形成时的模拟流程说明，技术差异按本入口第3节和源码裁决读取。 |

直接下载某一HTML即可体验；要使用相邻PNG/交接跳转，完整下载设计目录。全部原始ZIP字节保存在[分片归档](bundle/README.md)，恢复后与原ZIP完全一致。完整载荷由[原MANIFEST](MANIFEST.json)校验；本次仓库增补由[全仓manifest](../../manifest.json)登记。

## 2. 数量口径：45个状态、48张图、95个原文件

95是原交付目录的物理文件数，**不是95张页面或95份独立设计**。同一交接的PDF/HTML/Markdown、源码、QA与重复ZIP都计入了文件数。

| 原交付分类 | 数量 |
|---|---:|
| 高清PNG：45张3200×1800、3张2732×1536紧凑适配 | 48 |
| 原型、图集、交接等可打开文件，加原README/MANIFEST | 9 |
| 原型源码、重建脚本、字体与许可 | 11 |
| 字段/操作迁移、来源与图像索引材料 | 13 |
| 原型走查与交付检查记录 | 12 |
| screens/manifest.json | 1 |
| 重复ZIP整包 | 1 |
| **原交付文件总数** | **95** |

仓库内直接保留94个展开文件；42MB重复ZIP以11个分片及恢复资料保存，全部原字节均可还原。仓库入口、源码裁决、工作包、提示词和发布回执是本次新增的实施资料，不能再加进“界面数”。输入附件另有36张截图、35项唯一图像；旧仓库v1.1另有34张导出图，两者均不是本次新设计的95文件口径。

## 3. 使用最新代码修正模拟含义

原型用于演示完整任务及状态，不是生产接口规格。复核固定在main `e2e69292899c709056ce9ab6159bf01f6f18de6c`；执行实现时继续继承最新main。以下差异必须在WPF实施中落实：

| 原设计模拟内容 | 最新真实代码裁决 |
|---|---|
| 右相机端口填写abc，再改9001 | 海康reader实际只有IP、预期序列号、实体关联模式、编码、队列容量五字段，没有端口；用该驱动真实非法IPv4/整数演示相同错误闭环。 |
| 相机对象 | 左右读码走observation/Reader及Topology.Readers；可选采图Camera是另一个能力，Sportmaster不因此增加包裹预览。 |
| 保存与运行版本独立自增 | 生产沿唯一配置信封Revision、完整候选CAS、内容指纹、RuntimeId及确认记录；不新增三套版本账。 |
| 保存失败、应用和恢复 | 保存返回Rejected也可能发生在落盘后，必须核对真实结果；现行运行参数按Restart处理。单对象Reconnect不应用Pending；Effective字段存在也不证明已启动、健康或准入。 |
| WCS诊断与“查询原请求”成功 | 当前RCS无查询合同，Health只证明本地客户端状态；查询直接说明不可用，不调用POST冒充查询或安全测试。连接恢复不消除原件责任。 |
| 模板导入包含地址 | 当前安全模板排除设备/IP/目标/凭据/编号等；完整备份恢复是独立流程，不混成模板。 |
| 标题、安装和首次配置 | 继承已批准“菜鸟供包台”品牌、单EXE、原向导/身份/目录/快捷方式及BUILD-INFO，不重建安装引擎。 |

完整真实RCS参数、Application策略、状态和守卫见[源码裁决](../../design/sportmaster-task-redesign-01.md)。未知业务合同和现场条件如实列依赖；完成不受影响界面和准确的不可用反馈，不能让模拟成功掩盖缺失能力。

## 4. 高清图逐张入口

每个链接均打开独立PNG；[图像索引](screens/manifest.json)保存完整尺寸与场景对应。概览不替代大图。

| 任务/状态 | 独立高清图 |
|---|---|
| 正常连续作业 | [正常双位](screens/01-work-normal.png)、[等待](screens/02-work-waiting.png)、[处理中](screens/03-work-processing.png) |
| 作业异常 | [WCS中断](screens/04-work-wcs-outage.png)、[单相机中断](screens/05-work-camera-outage.png)、[结果未知](screens/06-work-unknown.png)、[快照过期](screens/07-work-stale.png) |
| 长数据/适用登录 | [长内容](screens/08-work-long.png)、[可选登录](screens/09-work-login.png) |
| 配置对象与编辑 | [概览](screens/10-settings-summary.png)、[未修改](screens/11-camera-clean.png)、[已编辑](screens/12-camera-dirty.png)、[字段错误](screens/13-camera-invalid.png) |
| 保存与生效 | [已保存待生效](screens/14-camera-pending.png)、[应用检查](screens/15-apply-review.png)、[应用失败](screens/16-apply-failed.png)、[运行未知](screens/17-runtime-unknown.png) |
| 服务与绑定 | [WCS连接失败](screens/18-wcs-failed.png)、[位置绑定](screens/19-bindings.png)、[关联冲突](screens/20-bindings-conflict.png) |
| 规则与组件 | [运行规则](screens/21-policies.png)、[已安装组件](screens/22-modules.png) |
| 记录 | [查询](screens/23-history.png)、[日期错误](screens/24-history-invalid.png)、[空结果](screens/25-history-empty.png) |
| 诊断与离开 | [中断诊断](screens/26-diagnostics-outage.png)、[恢复核对](screens/27-diagnostics-recovered.png)、[未保存离开](screens/28-unsaved.png) |
| 首次配置 | [工作站](screens/29-setup-identity.png)、[设备](screens/30-setup-devices.png)、[检查](screens/31-setup-check.png)、[保存待启动](screens/32-setup-pending.png) |
| 安装 | [目录](screens/33-install-directories.png)、[复核](screens/34-install-review.png)、[完成](screens/35-install-complete.png) |
| 模板/恢复 | [模板预览](screens/36-template-preview.png)、[备份恢复](screens/37-backup-recovery.png) |
| 支持摘要 | [导出预览](screens/38-support-preview.png)、[范围错误](screens/39-support-invalid.png) |
| 格式/修订冲突 | [旧格式核对](screens/40-legacy-review.png)、[并发修订](screens/41-reconcile.png) |
| 执行中与失败 | [保存失败](screens/42-config-save-failed.png)、[保存中](screens/43-config-saving.png)、[应用中](screens/44-config-applying.png)、[连接检查中](screens/45-camera-testing.png) |
| 紧凑适配 | [作业](screens/compact-work-normal.png)、[字段错误](screens/compact-camera-invalid.png)、[连接失败](screens/compact-wcs-failed.png) |

## 5. 状态与验收

源截图可见事实、待验证交互和建议新增能力在原交接及迁移表分开。handoff/data/source-gallery.md保留原附件中的D盘开发路径；需要查看原截图时使用[仓库原图图集](../../archive/sportmaster-native-gallery-20261020-0e79e73/index.html)，不要依赖原开发机路径。原型六核心任务及后续10项定向复核按各记录中的冻结版本判断；本次未重跑其全量交互，也没有新增WPF或设备PASS。最终原型SHA256为 `50e9efb8efe30325ffcd8c42fe3b47fda0930e02a056e2f24304e7592252a4c8`。

原设计v1文件不因最新源码复核而改标；例如原README的“本次没有读取冻结提交源码”只描述设计形成时输入。本次另有真实源码分析，不能反过来修改旧检查记录。最新品牌、字段/能力、配置和安装的实施要求统一由本入口及裁决补充。

当前源码、既有233项WPF合成检查、7c介质41项实际Simulation升级、真实设备与现场分别见[UI状态](../STATUS.md)及[本次发布记录](../../runtime/ui-task-redesign-01-publication.md)。新UI必须有自己的原生大图、六任务结果和实际验收边界。旧设计退出本包当前布局定义，但原件、历史失败和REQ/BR/CAPI/D/RV保持。
