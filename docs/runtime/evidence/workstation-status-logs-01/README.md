# WORKSTATION-STATUS-LOGS-01 原生验证

工程源冻结为 `298783782223a234a402015d69a66af68d93a79a`。最终结果与构建身份在同目录收口；后续文档提交不改变该次二进制身份。

原生自动控件验证使用实际Windows HWND/Dispatcher，系统DPI为125%，覆盖1366×768、1092×614和800×420客户区。状态、实体、账号与日志均为合成数据；连接色块投影与真实Application/SQLite/loopback回包链分别记录。物理键鼠、其他系统缩放、目标工控机、SDK/真实相机/RCS和现场验收未运行。

`initial/`保留开发期间的失败：新增测试的客户区名称与NameScope设置、日志级别控件定位不再唯一、旧空态扫描隐藏断言被本轮产品决定替代，以及真实最小窗口下一步说明被长拒绝原因挤出。最后一项通过固定下一步说明、压缩共享RCS状态行修复；保持原业务与恢复断言。原失败不倒写成PASS。

所有用户截图、现场目录、账号材料与四份既有未跟踪原稿留在D盘本地，不在本证据包。下列图片均为本轮自动原生合成截图，不能视作现场联调证据。

## 冻结结果

| 检查 | 实际结果 |
|---|---|
| 新作业状态/入口/终端专项 | 38 PASS |
| 既有层级、尺寸、状态与完整回包链 | 239 PASS |
| 既有管理模式、草稿、日志恢复 | 57 PASS |
| 日志有界多行上下文/迟到撤权 | 2 PASS |
| 原生合计 | **336 PASS** |
| 原生Release构建 | 0警告、0错误 |
| 发布指南与不可变链接 | 3项PASS |
| Sportmaster桌面发布输出 | publish成功；实际输出运行 `--environment-report` 退出0 |
| 原生/发布关键库 | Platform、Contracts、ParcelFlow、两个Desktop及Sportmaster共6个DLL逐字节SHA256相同，见IDENTITY.json |

发布启动冒烟仅验证宿主与环境报告入口；正常作业窗口及状态由上述原生测试验证。没有执行安装器替换现有安装，也没有将环境兼容报告当作生产准入成功。发布与原生环境为本机Windows 11 23H2 x64、.NET 10.0.13，125%实际DPI；目标工控机和其他OS版本不由此次结果覆盖。无新运行依赖或配置格式迁移。

Impeccable按WPF Operate适配完成实物差距、信息/文案、交互与最终渲染四轮检查；ui-ux-pro-max独立WPF查询用于键盘与Dispatcher约束。没有用网页检测替代原生验收；原生控件自动调用不等于物理键鼠或代表性操作员验收。

## 实际原生截图

- [常规双位结果](work-results-1366.png)、[最小窗口结果](work-results-800.png)、[最小窗口空态](work-empty-800.png)
- [合成单侧离线与共享RCS离线](connection-offline.png)、[明确待配置](connection-deferred.png)
- [终端日志](logs-terminal-1000.png)、[最小终端窗口](logs-terminal-800.png)
- [真实Application加loopback回包链：新条码筛选拒绝且另一位原结果保持](chain-invalid-after-slot-800.png)

原始日志、发布环境报告、冻结二进制身份和FILES.json同目录；用户附件未复制到此处。初次失败与最终PASS分别保留，历史D/RV及发行SourceCommit不改写。CI另按PR实际状态判断，不预记PASS。
