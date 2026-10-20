# PR57 / PR58 不测试合并与上传产物保管

2026-10-20 用户明确要求不测试，将刚上传代码/报告/文档提交 PR 并合并全部开放 PR。开工远端 main 为 1e97740f73fa1714505a128283f3df5bb96a5518。刚上传的视觉产物已在 PR58（原 head 4e23f01405f0c3c5a443711bb663bad76bce5f37），不创建重复 PR；另一个开放项为 PR57（原 head fd13f30242138020bd3810970cc372ae6a6a393b）。本地仅有报价表未跟踪，继续排除，不覆盖其他工作树。

## 实际合并与冲突裁决

PR58 已按原 head 实际合并为 e060c4405c16a97cd2e9d7e3bc6ea92cb600182c，包含已上传原生 WPF 代码、项目 skill、合成原生截图、报告和 manifest。未重新修改界面或宣称重跑原生验收。

PR57 分支继承该 main 后，仅 INDEX、manifest 及 release-windows.py 发生内容冲突。保留 PR58 当前视觉入口、默认新版策略和商业守卫；PR57 原八包资料作为 55025ce 历史来源继承，不覆盖当前代码、批准或旧资产。

统一发行器保留双方参数：complete 八包与 sportmaster 双包 profile、同 profile 的集合门槛、独立原清单和同源 ZIP 复用。记录中的 Profile 和 SourceTree 分别指向选定组合与实际运行源码，而非构建控制器；需要补构建时只按选定 profile 判断是否建立隔离来源 checkout。保留来源祖先/输入兼容、文件/ZIP 身份及受限清理机制；没有第二套发行入口。双方原测试与断言由合并保留，本轮未执行。

manifest 沿最新 main 合并实际路径和 PR57 新证据；保留未变历史对象/记录，修正受影响 Git blob、SHA 和大小，manifest.sha256 同步。原 D/RV、原 HTML 和历史批准不倒写，Impeccable 原 LICENSE/NOTICE/固定来源/WPF 适配保持。

## 本轮验证及发布边界

测试、构建、打包、运行、安装、WPF 交互和现场验证全部 NOT_RUN_BY_USER_REQUEST。仅使用 Git 内容/来源核对及 GitHub 普通 expected-head merge API；不强推、不改保护规则/仓库可见性、不触发手动发行工作流、不上传本地介质或覆盖旧 Release。

55025ce 已发布八包、dfc2ab6 的视觉本地冻结介质及其他旧包各自固定原源码。最终整合源码没有新介质或新的运行 PASS；不能使用旧 ZIP 宣称含本次全部最新 UI。Windows 目标基线/UAC/第二账户/DPI/真实 SDK/WCS/设备/现场仍按来源报告 NOT_RUN/BLOCKED。

最终 PR57 的实际 merged 状态、merge SHA、main SHA 和相对开工 main 的去重提交数量由本次 merge API 完成后回读报告；本文件不伪造自身后继 merge SHA。计数采用 git rev-list <开工main>..<最终main>，区分普通和合并提交，不相加分支 ahead。
