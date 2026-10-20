# V2 字段与协议资料

这些文件用于设计迁移和实现核对，**不是可直接导入生产的配置**。所有示例地址、标识、编号与业务数据都用于离线展示，本包没有连接相机或 RCS。

| 文件 | 内容与计数 |
|---|---|
| [ui-v2-field-migration.csv](ui-v2-field-migration.csv) / [JSON](ui-v2-field-migration.json) | 54 条当前配置及数据字段，包含中文名称、归属、控件、格式/范围、依赖、生效、实现状态和代码出处；两种格式同内容。 |
| [ui-v2-original-field-overlay.csv](ui-v2-original-field-overlay.csv) | V1 原 P01～P58 字段/状态逐项去向，与新四类配置及保留任务的关联；不是58张页面。 |
| [rcs-request.example.json](rcs-request.example.json) | 转写本轮用户给出的请求示例，移除字段值旁非 JSON 的中文批注；字段归属在映射表说明。 |
| [rcs-response.example.json](rcs-response.example.json) | 转写本轮用户给出的成功响应示例，保留 `waveNo`、`taskId`、`skuId` 以及字符串类型的返回 `quantity`。 |

代码核对基准：`ba80d0ea90d86b24f9d9eebd20d075da34320972`。用户最新四类归属优先；源码用于确定迁移起点和不能误改的守卫。下一轮先读取最新 main，若合同变化，更新映射而不是机械复制原型内存对象。

## 需要特别保留的区别

- 系统的本地 `StationId` 是稳定本机身份；RCS `stationCode` 是业务编码。二者不可互换。
- 驱动配置中的取格口 `bcrCode` 是 Application 的实例映射；心跳 `bcr-code` 已是驱动第6参数。两种用途并列，不能相互覆盖。系统心跳 `StationCode` 同样与取格口 `stationCode` 分开。
- 新增已实现的 `BarcodeFilter.Pattern` 与心跳间隔；复用 Application 本地测试、规则冻结和候选证据。54行包含4个新增真实配置/策略字段、1个临时样例输入和1组只读候选证据，不代表54个独立可写参数。
- `specifiedChute=false` 时不携带不适用的保存键，发出报文的 `chuteCode` 为 `null`；重新启用时条件必填。
- 关闭计划备份对应真实 `BackupSchedule=null`，不删除已有备份；原型扁平布尔字段仅用于演示。
- 请求 `quantity` 是整数，示例响应 `quantity` 是字符串。缺返回值不使用请求默认或上一件值补齐。
- Int64 标识与请求编号保持整数原文和 JSON 数字 token，不经 JavaScript 浮点数往返。
- `length/width/height/weight` 为 `null` 不构成新增称重、图片预览或测量功能的依据。

未知默认值、驱动现场资格、RCS 业务拒绝/缺字段的确切协议处理不能从成功示例推断。字段范围只引用当前实际合同；示例数值不是部署建议。
