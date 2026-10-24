# 原生WPF前后证据

基线 `c2300cd` → 实现 `8bf2a9d`；本机实际125% DPI，合成端口及数据。客户区以DIP计；截图像素按1.25倍输出。完整方法、验证边界及运行命令见[运行记录](../../task-content-hierarchy-01.md)。

## 同场景对照

| 布局 / 页面 / 客户区 | 修改前 | 修改后 |
| --- | --- | --- |
| classic / 工作台 / 1366×768 | [前](before/classic-工作台-1366.png) | [后](after/classic-工作台-1366.png) |
| classic / 工作台 / 1092×614 | [前](before/classic-工作台-1092.png) | [后](after/classic-工作台-1092.png) |
| classic / 工作台 / 800×420 | [前](before/classic-工作台-800.png) | [后](after/classic-工作台-800.png) |
| classic / 作业记录 / 1366×768 | [前](before/classic-作业记录-1366.png) | [后](after/classic-作业记录-1366.png) |
| classic / 作业记录 / 1092×614 | [前](before/classic-作业记录-1092.png) | [后](after/classic-作业记录-1092.png) |
| classic / 作业记录 / 800×420 | [前](before/classic-作业记录-800.png) | [后](after/classic-作业记录-800.png) |
| classic / 参数设置 / 1366×768 | [前](before/classic-参数设置-1366.png) | [后](after/classic-参数设置-1366.png) |
| classic / 参数设置 / 1092×614 | [前](before/classic-参数设置-1092.png) | [后](after/classic-参数设置-1092.png) |
| classic / 参数设置 / 800×420 | [前](before/classic-参数设置-800.png) | [后](after/classic-参数设置-800.png) |
| classic / 日志查询 / 1366×768 | [前](before/classic-日志查询-1366.png) | [后](after/classic-日志查询-1366.png) |
| classic / 日志查询 / 1092×614 | [前](before/classic-日志查询-1092.png) | [后](after/classic-日志查询-1092.png) |
| classic / 日志查询 / 800×420 | [前](before/classic-日志查询-800.png) | [后](after/classic-日志查询-800.png) |
| supply-station / 工作台 / 1366×768 | [前](before/supply-station-工作台-1366.png) | [后](after/supply-station-工作台-1366.png) |
| supply-station / 工作台 / 1092×614 | [前](before/supply-station-工作台-1092.png) | [后](after/supply-station-工作台-1092.png) |
| supply-station / 工作台 / 800×420 | [前](before/supply-station-工作台-800.png) | [后](after/supply-station-工作台-800.png) |
| supply-station / 作业记录 / 1366×768 | [前](before/supply-station-作业记录-1366.png) | [后](after/supply-station-作业记录-1366.png) |
| supply-station / 作业记录 / 1092×614 | [前](before/supply-station-作业记录-1092.png) | [后](after/supply-station-作业记录-1092.png) |
| supply-station / 作业记录 / 800×420 | [前](before/supply-station-作业记录-800.png) | [后](after/supply-station-作业记录-800.png) |
| supply-station / 参数设置 / 1366×768 | [前](before/supply-station-参数设置-1366.png) | [后](after/supply-station-参数设置-1366.png) |
| supply-station / 参数设置 / 1092×614 | [前](before/supply-station-参数设置-1092.png) | [后](after/supply-station-参数设置-1092.png) |
| supply-station / 参数设置 / 800×420 | [前](before/supply-station-参数设置-800.png) | [后](after/supply-station-参数设置-800.png) |
| supply-station / 日志查询 / 1366×768 | [前](before/supply-station-日志查询-1366.png) | [后](after/supply-station-日志查询-1366.png) |
| supply-station / 日志查询 / 1092×614 | [前](before/supply-station-日志查询-1092.png) | [后](after/supply-station-日志查询-1092.png) |
| supply-station / 日志查询 / 800×420 | [前](before/supply-station-日志查询-800.png) | [后](after/supply-station-日志查询-800.png) |

## 异常与交互场景

以下额外截图配合[专项断言日志](after/WINDOWS-WPF.log)阅读；不能仅由截图推断守卫通过。

- [classic-diagnostics-800](after/classic-diagnostics-800.png)
- [classic-login-1366](after/classic-login-1366.png)
- [classic-logs-unknown-800](after/classic-logs-unknown-800.png)
- [classic-long-blockers-800](after/classic-long-blockers-800.png)
- [classic-long-blockers-start-800](after/classic-long-blockers-start-800.png)
- [classic-Rejected-800](after/classic-Rejected-800.png)
- [classic-stale-800](after/classic-stale-800.png)
- [classic-Unknown-800](after/classic-Unknown-800.png)
- [supply-station-login-1366](after/supply-station-login-1366.png)
- [supply-station-logs-unknown-800](after/supply-station-logs-unknown-800.png)
- [supply-station-Rejected-800](after/supply-station-Rejected-800.png)
- [supply-station-Unknown-800](after/supply-station-Unknown-800.png)

before仅运行共同场景，不对旧实现执行新增布局断言。after共85项PASS，既有参数回归30项、内部页回归25项；构建、发布构建及环境报告入口日志在regression/。没有物理输入、其他OS缩放、安装、真实设备或现场验收证据。

FILES.json列出除自身外本目录所有Git blob的SHA-256（文本按Git换行规范化后的字节；Windows检出换行可能不同）；RESULT.json固定生产源码基线、实现身份及变更源文件哈希。
