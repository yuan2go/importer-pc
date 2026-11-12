# IE01 CI 与 main 保护

当前 PR/main 自动入口是 [industrial-core.yml](../../.github/workflows/industrial-core.yml)，变更选择与结果汇总在 [industrial_ci.py](../../scripts/industrial_ci.py)。工作流始终响应 PR 和 main 提交，不在工作流事件上排除文档；只使用只读仓库权限，不使用发布或现场密钥。

## 自动检查按实际输入选择

`repository-checks` 每次执行，读取完整 Git diff，并检查当前入口链接、故障码说明、实际交付文档和 CI 选择逻辑。PR 比较事件中的 base 与检出的合并结果，main 提交比较事件中的 before 与 HEAD；checkout 获取完整历史。使用 `git diff --no-renames -z` 同时考虑重命名前后和删除路径，不使用有文件数限制的变更列表 API。比较基准不可用时执行完整回归；脚本自身错误使检查失败，不解释为“没有变化”。

| 变更内容 | 自动执行 |
|---|---|
| 仅 `AGENTS.md`、`README.md`、`CHANGELOG.md` 或 `docs/` 下普通 Markdown | 文档与选择检查、最终汇总；不启动 Windows 编译任务。 |
| 仅上述文档及 `docs/operator/*.md` | 另编译 Sportmaster 桌面。操作手册由 `Ipc.Application.Sportmaster.Desktop` 内嵌，不能按纯说明文件跳过；此时不重跑无关业务和窗口行为。 |
| 代码、测试、配置、脚本、工作流、构建设置、非 Markdown 资料或任何未识别路径 | 完整核心回归、真实子进程行覆盖、分析和发布工具边界、格式检查、三个桌面装配及 SM 原生窗口行为。`docs/ui/tokens/tokens.json` 是程序输入，属于这一类。 |
| 无法取得可靠 Git 比较基准 | 完整回归。 |

这里不维护通用项目依赖图，也不根据文件夹名称猜测共享代码是否影响其他 Application。代码变更继续保留 Sportmaster、DirectPlc、WcsToPlc 三个现有编译装配；后两项不表示恢复其功能开发。

核心测试由 `tests/Ipc.Core.Tests` 正常发现和执行，包括当前到位分轮、单一 StationCode 和原请求边界，不在 YAML 另抄套件名、方法名或任意最少测试数。产品覆盖工具统一核对 TRX 完成终态、非空、发现/执行/通过/结果行数一致及无跳过，再检查四个真实产品程序集的行覆盖。格式检查覆盖 `src` 和 `tests` 中的 C# 源文件，数量随代码变化，不固定文件数。原生窗口继续核对实际进程退出和成功/失败证据，保留日志与截图。

原有分析、发布策略和 Windows 进程释放测试继续自动执行；本地 RCS 模拟器的既有回环测试也在代码/工具改动时执行，核对双位到位推进、原请求重放、重启和协议拒绝。模拟器通过不等于客户 RCS 或现场通过。文档发布测试统一放在 `repository-checks`，不在 Windows 核心任务再执行一次。

各独立测试工程仍使用 locked restore；现有 `dotnet build` 使用增量构建。没有将未还原的工程直接改成 `--no-restore`，也不跨 runner 复用配置不同的二进制。真实 SDK、设备、UAC/SCM、完整安装生命周期和现场长稳仍有独立边界。

## 手动专项独立选择

Industrial core 的 `scope` 默认是 `regression`：

| scope | 执行范围 |
|---|---|
| `regression` | 完整自动回归范围，不依赖变更分类。 |
| `branches` | 独立构建并在二进制副本上执行核心和相应 WPF 场景，采集真实分支证据；不再同时启动普通回归矩阵或 PDF。 |
| `print` | 以哈希锁定的依赖生成和核对可搜索 A4 PDF；不启动业务构建或分支采集。 |
| `all` | 明确运行完整回归、分支采集和 PDF。 |

所有选择都保留文档/选择检查和最终汇总；并发取消按 ref 和 scope 区分，独立专项不会互相取消。PDF 和分支证据不属于每次 PR 的必需交付物，相关源文件和报告解析器的检查仍保留。

[sportmaster-commercial.yml](../../.github/workflows/sportmaster-commercial.yml) 也是手动入口，按 `contracts`、`installation`、`delivery`、`entrypoints` 或 `all` 选择，默认 `contracts`。SDK 与完整 Windows 发布仍使用各自入口；[SM 离线运行库安装](../../.github/workflows/sm-runtime-install.yml) 继续按其真实依赖路径自动触发。不能把这些手动任务算成每次 PR 都执行，也不能用普通回归替代其原生边界。

## 固定汇总检查与建议保护配置

汇总任务使用 `if: always()` 等待 `repository-checks`、核心回归、桌面矩阵以及两项手动专项。它先要求 `repository-checks` 成功，再按计划逐项要求：选中的任务必须 `success`，未选中的任务必须 `skipped`。失败、取消、应执行却被跳过、缺失输出和路由错误都不能成为绿色汇总。自动 PR/main 的检查名固定为 `industrial-ci`；手动运行使用 `industrial-ci-manual-<scope>`，避免仅打印或分支专项的成功与同一提交的自动回归共用必需检查名。原核心任务名称和代码变更时的三个矩阵名称保留。

[main-required-checks.json](../../.github/main-required-checks.json) 建议将稳定的 `industrial-ci` 作为必需状态检查，并保留 `strict: true`，来源绑定 GitHub Actions（App ID 15368）。这样普通说明文档无需制造空 Windows 任务来凑齐矩阵名称；内嵌手册可以只编译实际受影响的 SM 装配。

**该 JSON 只是仓库内的建议配置，不会修改 GitHub 分支保护；本次没有应用远端规则。** 旧文档的 2026-11-03 只读结果不是当前保护状态的证明。应用前须重新读取现状。如果远端仍要求旧的逐项矩阵检查或 `operator-print`，必须明确将本项目这组必需检查调整为新的汇总门；动态选择不会保证未选中的矩阵项出现同名结果，不能声称自动兼容旧配置。

仓库所有者应用时，在 main 保护规则中选择 `industrial-ci` 和 GitHub Actions 来源，保留严格更新要求、其他有效必需检查、审阅要求、限制及绕过规则。JSON 只描述状态检查部分，不能覆盖整份保护配置。保存后读回规则，并核对待合并 PR 的同名结果；未经读回，不记录为已完成。

GitHub 对条件任务的跳过语义见[官方说明](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions)，保护规则接口见[官方文档](https://docs.github.com/en/rest/branches/branch-protection)。固定汇总门负责区分合法选择与实际失败，不能仅因为 GitHub 将某个跳过任务显示为成功就视为全部验证完成。
