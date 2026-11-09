# 项目级 skills

WPF 界面审查优先使用 [Hallmark-WPF](hallmark-wpf/SKILL.md)；实现资料查询可按需使用下列 UI UX Pro Max 或 Impeccable。不自动串行执行三套流程，所有建议服从根目录 AGENTS.md 和现行设计。

## Hallmark-WPF（本项目）

这是基于 Nutlope/hallmark 2.0.0 的轻量方法适配，不是上游全量安装。固定来源、差异与许可见 [UPSTREAM.json](hallmark-wpf/UPSTREAM.json) 和 [LICENSE](hallmark-wpf/LICENSE)。只加入文本规则，无新增运行、构建或发布依赖；不覆盖原有 skills，不安装到用户全局目录。

在包含这些文件的分支/工作树中读取本 skill；合入 main 后才成为 main 的入口。直接指定 `$hallmark-wpf`，或明确要求读取 `.agents/skills/hallmark-wpf/SKILL.md`。本轮不声称已验证某台本机客户端的自动发现。

```text
$hallmark-wpf audit SM作业页、导航和退出确认；只报告问题，不修改代码。
$hallmark-wpf refine 退出确认；保持现行紧凑布局和业务语义，落实修改并做必要原生验证。
```

`audit / refine / study` 是本地工作模式，不是 shell 命令。默认只读；只有明确授权才修改代码。审查方法、现有验证入口和短报告格式见 [WPF审查方法](../../docs/design/wpf-ui-review.md)。不运行网页主题选择、浏览器 sloplint 或设计 engine；没有 Windows 时仅报告静态结论与待验证项。

## UI UX Pro Max

已安装 [UI UX Pro Max](ui-ux-pro-max/SKILL.md)，供本项目的UI/UX设计、实现与审查使用。下次对话可直接指定 `$ui-ux-pro-max`，或要求使用该skill处理UI任务。

## 来源与维护

- 上游：[nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/blob/56ea9cf174ed86786187daa9de0e6247cb0747e4/README.zh.md)。固定提交：`56ea9cf174ed86786187daa9de0e6247cb0747e4`。
- 使用 `skill-installer` 从上游 `.claude/skills/ui-ux-pro-max/` 安装完整核心skill；目录遵循上游Codex配置 `.agents/skills/ui-ux-pro-max/`，仅作用于本项目。包含搜索脚本、数据、参考说明与上游测试文件。
- 上游 [MIT许可证](ui-ux-pro-max/LICENSE)仅覆盖此引入内容，不改变本项目或客户资料的许可。数据中含字体目录元数据和来源链接，没有字体二进制。
- 本地适配仅涉及 `SKILL.md`：将Claude插件变量路径改为skill目录内的相对命令，使用 `python3`，补充本项目WPF、UI基线及验收边界。脚本、数据、参考说明和上游测试保持原字节。逐文件来源与校验值见 [UPSTREAM.json](ui-ux-pro-max/UPSTREAM.json)。
- 更新时先核对上游提交与许可，再显式升级并重做搜索冒烟；保留本地适配，不自动跟随上游main或覆盖既有设计文件。

## 使用

已有Python 3即可运行，搜索使用标准库和本地数据。以下命令从项目根目录执行：

```bash
python3 .agents/skills/ui-ux-pro-max/scripts/search.py "binding command validation" --stack wpf
python3 .agents/skills/ui-ux-pro-max/scripts/search.py "keyboard focus modal" --domain ux

```

这些命令只输出建议。仅在任务确需保存设计建议时使用 `--persist`，并显式指定 `--output-dir <项目根目录>`；先读取已有文件，未经明确授权不使用 `--force`。

页面修改仍须先按根目录AGENTS读取UI实物基线及完整交接。WPF建议不意味着迁移UI框架；生成结果不替代现有设计、客户规则、业务命令守卫或验收证据。

## 安装核验

```bash
python3 .agents/skills/ui-ux-pro-max/scripts/validate_data.py
python3 .agents/skills/ui-ux-pro-max/scripts/tests/test_core.py
python3 .agents/skills/ui-ux-pro-max/scripts/tests/test_native_desktop_stack_freshness.py
```

上游部分测试依赖其完整仓库中的CLI、刷新工具及测试夹具，不能将本项目的skill目录当作上游完整开发仓库运行全套测试。安装核验只证明本地skill的数据完整性和搜索可用性，不是业务程序、Windows交互、SDK或现场验收。

2026-10-19安装核验：macOS/Python 3.12.13；12个领域数据文件、22个技术栈文件及推理表校验通过；核心39项和原生/桌面指南9项测试通过。WPF、UX和设计系统三种JSON输出冒烟通过，绝对脚本路径在项目目录外可用，未持久化生成设计文件。74份上游文件中73份保持原字节，仅 `SKILL.md` 作上述适配。业务构建及Windows/SDK/现场验证本次未运行。

完整 `git diff --cached --check` 报告上游 `scripts/design_system.py` 原有的46处行尾空格；该文件已按上游Git blob核对原字节，未为格式检查改写。仅本地新增说明和适配文件的格式检查通过。

## Impeccable（本项目）

[impeccable](impeccable/SKILL.md) 从官方 [pbakaus/impeccable](https://github.com/pbakaus/impeccable/tree/46b822f7dda9f7dba799a9c7776fdc19b3a93559) 的 `.agents/skills/impeccable` 完整安装，固定提交46b822f7dda9f7dba799a9c7776fdc19b3a93559，skill4.7.0 / engine0.1.16。Apache-2.0 LICENSE及第三方NOTICE完整保留，全部原文件hash见UPSTREAM.json；原ui-ux-pro-max保留。

本地只适配SKILL.md入口并新增[WPF约束](impeccable/WPF.md)。下一轮Codex可识别 `$impeccable`，本轮已读取并实际应用其Operate、critique/layout/clarify/polish。原生WPF按native评审扩展，不用网页检测/CDP/营销动效验收。Windows context启动器会按固定版本缓存校验过的engine；不入库或进入客户端依赖。本轮context实际执行，报告XAML未识别为视觉实现，故以项目实物和真实WPF为权威。

`--design-system`模式忽略`--stack`；WPF查询必须独立执行，不把通用设计系统输出称为WPF定稿。示例：`python .agents/skills/ui-ux-pro-max/scripts/search.py "binding update focus selection" --stack wpf`；针对性UX查询见本轮证据。技能安装/搜索可用不代表客户端验收。
