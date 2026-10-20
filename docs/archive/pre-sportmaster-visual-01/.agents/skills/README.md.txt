# 项目级 skills

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
python3 .agents/skills/ui-ux-pro-max/scripts/search.py "industrial operations dashboard" --design-system -p "Importer-PC"
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
