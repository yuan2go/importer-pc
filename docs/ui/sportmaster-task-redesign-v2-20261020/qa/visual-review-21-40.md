# Sportmaster UI V2：21–40 号高清图逐张视觉复核

**最终状态：F-01、F-02 已在末尾记录的四张最终图复核中关闭。下面的首轮问题保留为修改依据，不是最终尚未修复问题。**

- 日期：2026-10-20。
- 范围：`screens.json` 的第 21–40 项，共 20 张 PNG；全部通过 `view_image(detail="original")` 单图实际查看，按 4 张一组输出，没有以拼图替代逐张查看。
- 图像规格：本组均为 3200 × 1800 像素，对应 1600 × 900 逻辑视口。
- 只读审查：未修改原型源、截图、manifest 或 production code。
- 证据边界：本报告只证明已查看这些离线设计图；按钮行为以原型走查记录为准，不构成 Windows、DPI 或真实链路验收。

## 实质问题

### F-01：运行未知时，固定底栏仍断言运行尚未更新

**需修正：`33-config-runtime-unknown.png`，参数页顶部状态与底部保存/生效栏。**

顶部写明“无法确认当前运行内容，禁止恢复接收。待生效配置仍保留。”，但底部仍显示“实际运行内容尚未更新，需要受控重启”，主按钮也仍是“受控重启并生效”。当运行内容未知时，不能断言内容尚未更新；下一步应先读取启动确认/核对运行配置，读取失败后进入对应诊断，不应把重复重启呈现为默认解决方式。

同一固定底栏在 `31-config-applying.png` 正在重启状态仍写“需要受控重启”。应随当前执行状态改为“正在受控重启，等待启动确认”，与顶部和忙碌按钮保持一致。

本问题已即时发送 root。这里只提出已观察到的状态矛盾；没有基于单图声称按钮实际可以绕过运行守卫。

### F-02：保存/生效失败缺少本次原因和对应下一步

**应补充：`28-config-save-failed.png` 与 `32-config-apply-failed.png`，失败提示区域。**

两图均清楚说明保存/运行结论，草稿和待生效候选没有丢失。但失败区域只呈现“已确认未写入”或“旧配置已确认”，没有说明这一次为何失败，也没有直达该原因的诊断/技术详情。全局“诊断与恢复”仍可进入，但图中不能判断会定位本次保存或启动错误。

补充一条与本次失败匹配的简洁原因、可复制的技术详情或对应诊断入口；保留当前已确认的落盘/运行状态，不使用未证实原因。避免用户只能再次点击保存或重启。

## 逐张查看记录

本组四类配置命名一致，36–40 均为独立首配五步且无日常左侧导航。可见中文、地址、对象标题和固定操作按钮清晰；未发现控件互相遮挡、模态按钮超出窗口或关键文字被组件覆盖。21、24、25、29、37、39、40 的长表单存在内容区滚动边界，固定标题和动作仍可见；未将需滚动的内容误记为布局遮挡。

| 编号 | 文件 | 查看结论 |
|---|---|---|
| 21 | `screens/21-driver-conflict.png` | 四类分类明确；右实例选中、重复 IP 与 bcrCode 同时关联到具体对象和字段，其他输入保留。 |
| 22 | `screens/22-driver-checking.png` | 正在检查对象明确为左实例编辑副本，检查按钮呈忙碌状态；未把编辑值检查称为设备或业务成功。 |
| 23 | `screens/23-config-validating.png` | 完整候选正在校验、字段和提交动作禁用；表单、对象归属和原运行状态仍可辨。 |
| 24 | `screens/24-components.png` | 一个 rcs-main 连接共用左右位；系统身份、实例映射和业务规则只读引用，四类归属一致。 |
| 25 | `screens/25-components-failed.png` | 失败对象和完整地址清楚；格式通过、TCP 失败、业务未验证分层显示，提供地址定位和技术详情。 |
| 26 | `screens/26-config-save-review.png` | 审阅弹窗完整呈现 1 个对象、2 项差异；整站软件暂停、排空、维护说明和保存后需重启均明确。 |
| 27 | `screens/27-config-saving.png` | 正在保存，输入仍可读；提交按钮禁用，新包接收已暂停，与保存前影响一致。 |
| 28 | `screens/28-config-save-failed.png` | 未写入结论与保留草稿一致；失败区域缺少原因和针对本次失败的定位入口，见 F-02。 |
| 29 | `screens/29-config-save-unknown.png` | 可能已经落盘与待核对明确；只提供核对保存结果，重复保存禁用，未断言保存成功或未写入。 |
| 30 | `screens/30-config-pending.png` | 修订 13 已保存待重启，与原运行内容区分；主动作是受控重启，软件接收仍暂停。 |
| 31 | `screens/31-config-applying.png` | 正在重启与禁用提交明确，但固定底栏仍沿用“需要受控重启”的待执行说明，见 F-01。 |
| 32 | `screens/32-config-apply-failed.png` | 候选待生效与已核实旧运行内容区分，接收继续暂停；失败原因和专用诊断下一步缺失，见 F-02。 |
| 33 | `screens/33-config-runtime-unknown.png` | 顶部运行内容未知与底部“实际运行内容尚未更新”的断言矛盾；主动作仍为重复重启，见 F-01。 |
| 34 | `screens/34-config-effective.png` | 修订 13 及运行配置已确认可辨，接收仍保持暂停；没有把保存/生效自动等同恢复接收。 |
| 35 | `screens/35-unsaved.png` | 三个离开选择及变更范围完整，保留草稿明确不变成运行修订；弹窗和按钮未遮挡。 |
| 36 | `screens/36-setup-module.png` | 无日常侧栏；五步完整，第 1 步同时呈现已安装模块、请求规则与应用预算；示例标识明确。 |
| 37 | `screens/37-setup-system.png` | 第 2 步系统身份含 zoneId、projectId、stationCode；本地身份与业务编码分开；计划备份属于系统。 |
| 38 | `screens/38-setup-devices.png` | 第 3 步同类型两个独立实例左右分列，IP 和业务编码明确；数量由 Sportmaster Application 决定。 |
| 39 | `screens/39-setup-component.png` | 第 4 步一个组件连接；endpoint、协议字段、编号区间与前置只读引用分组清楚。 |
| 40 | `screens/40-setup-review.png` | 第 5 步汇总对象并区分候选校验通过与真实链路未验证；主按钮仅保存首次配置。 |

## 对应图像身份

以下为本次实际查看批次的 SHA-256；后续若重新渲染应按新图复核相关问题，不能把本记录直接标为新版本通过。

| 文件 | SHA-256 |
|---|---|
| `screens/21-driver-conflict.png` | `f01dd8279371459bf4145e5eb545ed790e766ff625ab898d55a1018add542015` |
| `screens/22-driver-checking.png` | `b603ca54d5fee2497513df4b65b12119ad43660665a01361dd066efa8eac677a` |
| `screens/23-config-validating.png` | `8e6c949ba5836db8dd59bb7ad1382d4928a2034db498920bfe3d575587b8bb1f` |
| `screens/24-components.png` | `7753554b12750814915245907694cc97d051937526d92f69ae04d10641da50be` |
| `screens/25-components-failed.png` | `bd96d2f3c64d46f8dee61c6677dd7c7017c87619954802736b94b0ee39589daa` |
| `screens/26-config-save-review.png` | `38b22baa8a7748a377ed19bc84c6e297be227294e252e7f9a13034aec5b9a3d5` |
| `screens/27-config-saving.png` | `77287623deb1ae988ed26bb956f5c60ecdb2aad2d2a6516fb548cfd26872c511` |
| `screens/28-config-save-failed.png` | `1f8df4e7adeddfa69651520443ce8fef0b98e2ec6c7a140ffb6fc96d19f0f611` |
| `screens/29-config-save-unknown.png` | `80ac1ab011788f5e64415e7570b06e2f234775f33f58202bab550798c5679460` |
| `screens/30-config-pending.png` | `8892ac4274c46d4e84fef29a6d18a0093709f0316a23f86a26779e4df6fbe4cd` |
| `screens/31-config-applying.png` | `087f50f743ba17254be1ef6a3c3b862334934c1daf8ac4c0efdda5928578c0cb` |
| `screens/32-config-apply-failed.png` | `b4ae66d7d61463a77086f2c5d274867e348fd5bcf4b847b54a19e86bc4d6c340` |
| `screens/33-config-runtime-unknown.png` | `3b17af8f7e3010daa55128f34e93305a556c85db3f447d32cd419496fca26083` |
| `screens/34-config-effective.png` | `3fddb1c76539642a4a92ad1f69940b9cbd109ef0b5ef2915a9551ca1d77fc3cc` |
| `screens/35-unsaved.png` | `7f5e24111ae07aa9585624f3314a6a0bc2b125e1bf2651b90f156e93824542ec` |
| `screens/36-setup-module.png` | `7dc0ef7f2c9bea9fd54bdc099107dabc91b7f17ff916473cb7a95c2aec5fa59f` |
| `screens/37-setup-system.png` | `bf6190a5fc7cc2d62fe1e1875edc157cb3adebcd0f695da0e52a47687d22e1c9` |
| `screens/38-setup-devices.png` | `35e286111e579307538d3b271f56b7c77e8931f3fce16d70f38a44b6623523ee` |
| `screens/39-setup-component.png` | `9b6c29d78256e312917f2ff982026911b555098320b1b3d840c0be6872b8399f` |
| `screens/40-setup-review.png` | `74f677ae5f8d8fc9c86f63c324fc3f4bfe196541fe378b7c39a920ef47bdcedd` |


## 最终重渲染定向复核（2026-10-20）

按本轮收口范围，重新逐张以原始分辨率实际查看 28、31、32、33 四张 PNG；未重复其他页面或交互测试。以下结论仅对应本节列出的图像版本，保留前述首轮问题与哈希作为历史记录。

| 图像 | 实际观察与结论 |
|---|---|
| `28-config-save-failed.png` | 新增明确标注的示例原因“配置目录不可写，写入前拒绝”，给出检查权限后再保存的下一步，并提供“查看本次失败”。保留草稿、未写入与原运行内容已确认的表达一致。F-02 在此图关闭。 |
| `31-config-applying.png` | 固定底栏改为“正在应用已保存配置 · 整站范围”，说明正在停止并重建运行实例、等待启动确认；主动作禁用并显示“正在重启…”。实例和左右位均显示运行待确认，与顶部重启状态一致。F-01 在此图关闭。 |
| `32-config-apply-failed.png` | 新增明确标注的示例原因“候选相机启动失败”，同时表达旧运行证据已核实、候选保留、接收继续暂停；提供“查看本次失败”。底栏正确保留候选尚未投入运行的状态。F-02 在此图关闭。 |
| `33-config-runtime-unknown.png` | 底栏明确“当前运行内容尚不能确认，先核对启动与运行证据”，主动作改为“核对运行状态”；顶部禁止恢复接收与实例、左右位运行待确认保持一致。没有继续断言运行内容未更新，也没有以重复重启作为主动作。F-01 在此图关闭。 |

四张的新增文案、对象标题、参数和按钮均清晰可读，未观察到遮挡或关键文字截断。本轮两个问题在所复核的最终截图上已关闭，未发现新的实质问题。PNG 只能证明当前设计呈现，不能证明“查看本次失败”等按钮的真实行为、Windows/WPF 布局、设备链路或现场验收已通过。

| 最终复核文件 | SHA-256 |
|---|---|
| `screens/28-config-save-failed.png` | `19a1f6e70922ab21875ce22ada215d58918b19c60d8d1d639fc295f5a48f6ae7` |
| `screens/31-config-applying.png` | `ce2fb5b6ff88049107f661c8323b0de74de586dcdba3ba706fc442b676fae063` |
| `screens/32-config-apply-failed.png` | `678b4548b2508779b17aad56e1fd08d3d1188578bc17333fdf81e04e807ce737` |
| `screens/33-config-runtime-unknown.png` | `4d57193fd9adaa4c82e417cc28e265893511ffd44c6ed2e9c39e2110d6982751` |
