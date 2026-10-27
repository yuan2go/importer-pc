# PR129 同步最新 main

2026-10-27，PR129 原头 `479f1440` 合入 main `fdd24fa5`，继承 PR127 的 1.0.0 固定规则和 PR128 的海康生命周期/身份/诊断条码保护。仅文档索引及 manifest 冲突；保留两边当前入口与证据。历史日志、包及 SourceCommit 不改标。

27 个自有项目使用显式或继承的 1.0.0；129 处当前装配/产品/兼容声明均为 1.0.0。AGENTS、统一版本定义及全部依赖锁文件与该 main 相同。第三方 SDK/协议/schema 不因产品归一改号。

原头 CI 7/8，冻结验证中 TaskHierarchy02 仍要求默认显示 UTC 偏移。当前界面改为本机时间后，断言改为核对实际查询两端 EqualsExact、可读本机摘要、单一筛选滚动区的高级入口及固定查询动作可达；原任务、权限、焦点、草稿和防重断言保留，新增一项检查。原失败摘录保留，完整运行见 https://github.com/yuan2go/importer-pc/actions/runs/40493244928/job/122152151075 。

本机首次编译因修改定位到重复导航片段、缺少局部等待帮助函数失败；已从原文件恢复全部任务场景后局部修改，保留原编译失败日志，未删除断言换 PASS。最终 Release/locked 构建 0 警告、0 错误。

| 原生 WPF 模式 | PASS 行 | FAIL 行 |
|---|---:|---:|
| task-hierarchy | 236 | 0 |
| query-filters | 26 | 0 |
| operator-workspace | 30 | 0 |
| debug-log-live | 40 | 0 |

125% DPI、真实 HWND/Dispatcher、合成端口与 Application/SQLite。任务层级包含原生样式、任务闭环、日志、导航和受控状态；其余定向覆盖本次查询、作业引导和最新诊断保护。源码与日志哈希见 [核验清单](main-sync-verification.json)。远端完整 CI 须按同步后的 head 重新核对，旧头结果不转记新头。未安装替换、未连接真实设备，未声明现场或物理触摸验收。
