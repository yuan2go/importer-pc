# SM-CONTINUOUS-PDA-INPUT-01

2026-10-30，用户要求基于最新 main 修复 PR159 的 P1：[discussion_r4421108944](https://github.com/yuan2go/importer-pc/pull/159#discussion_r4421108944)。基线 `67ab87c1a1b146611504342c6ab080ed045231ec`（含 PR162），开发分支 `codex/fix-continuous-pda-barcode`。

主流程仍为键盘/PDA → 本窗草稿及基准 → `IParcelWorkstation.SubmitParcelBarcodeAsync` → Application 持久命令/原准入 → 原冻结请求 → 内置 RCS。人工输入不依赖相机在线，不伪造采集身份；SM 的原请求、会话/权限、配置修订、容量、Pending/Unknown、防重和原作业责任不变。

单一条码框未编辑时展示上一件结果；下一次文本输入/粘贴替换整条旧码。Admitted 后即使显示文本相同也准备整条替换，返回焦点同样准备；一旦成为草稿，普通逐字输入、插入/删除、焦点切换和刷新均保留草稿及选区。不抢其他控件焦点，不在刷新时生成命令。手动键盘可点击提交或回车；PDA 使用键盘字符和终止回车，输入类型仍仅为操作员声明，不以速度猜设备。

拒绝、Conflict、超限保持原保护；提交中/Pending/丢失回执不选择覆盖或解锁原输入，核对沿同一命令。Admitted 仅指独立作业已建立，不能据此解除业务 Unknown。

仅新增 Windows 原生定向入口，复用真实 Application/SQLite/内置 RCS 和既有合成 loopback 对端。逐键 SendInput A→Enter→B→Enter，中间不整体赋值 Text、不清空、不改选区；核对命令、作业、RCS wire loadCode 和第一件原冻结报文。包含手动修改、重复回车、提交中输入、Pending/丢失回执同命令核对及 Unknown 拒绝。既有草稿基准 Conflict 定向用例保留，不放宽现行断言。

当前索引修改前原字节及来源保存在 [archive](../archive/pre-sm-continuous-pda-input-01/manifest.json)。旧09/03报告及 PR159 另一项历史归档审查不在本包改写或宣称解决。必要 Windows 原生验证尚不可执行，保持草稿 PR；运行命令及证据边界见[记录](../runtime/sm-continuous-pda-input-01.md)。不启用/调度 GitHub Actions，不发布安装包，不关闭 D/RV 或现场资格。
