# 商业桌面设置参考与本项目取舍

2026-10-20实际阅读以下官方资料，并查看8张官方说明图片。研究没有安装和操作这些参考产品；界面行为来自文档，视觉观察来自官方截图。官方页面可能复用旧版或其他平台图片，不把它们宣称为最新 Windows 实机画面。

| 参考 | 官方依据 | 本项目采纳 | 不直接套用 |
|---|---|---|---|
| JetBrains Rider Settings | [设置对话框](https://www.jetbrains.com/help/rider/Settings_Preferences_Dialog.html)：目录、搜索、作用范围、提交动作 | 一个目录直达对象；稳定表单对齐；修改范围与主保存清楚 | IDE多层继承、Save To层级和复杂树；部分官方截图为macOS/旧版外观 |
| Microsoft PowerToys | [General settings](https://learn.microsoft.com/en-us/windows/powertoys/general)：分组、搜索定位、备份与诊断 | 自然语言名称、简短说明、错误原因；参数与具体动作相邻 | 宣传页头、大卡片留白、非必要外链；示例General截图显示v0.92.1，非当前版本证明 |
| Visual Studio 2026 | [Visual Studio settings](https://learn.microsoft.com/en-us/visualstudio/ide/visual-studio-settings?view=visualstudio)：类别/关键词搜索、适用范围和Modified筛选 | 搜中文、技术键和对象；结果保留路径并定位真实字段 | IDE多数立即生效的机制不适用于相机/RCS运行参数；“偏离默认”不等于本次未保存 |

实际查看的官方图片：Rider `SettingsSearch.png`、`options_for_layer.png`、`save_settings.png`、`code_style_settings.png`；PowerToys `general.png`、`settings-search-results.png`；Visual Studio `visual-studio-settings-search.png`、`visual-studio-settings-filter-options.png`。原图仅用于研究，未复制进本设计包。

项目级 Impeccable 的 Operate/WPF 适配及 craft-floor 用于本次审查。采纳规则为：先核对实际任务与现有产品、克制色彩、稳定对齐、完整交互状态、适合桌面的密度和真实 Windows 验证边界。没有使用营销网站结构、装饰仪表盘或流程工业大屏模板。技能建议服从用户确认的四类归属、业务合同和保存机制。

## 字体与公开素材

原生 WPF 使用 Windows 系统字体。离线原型为了在没有中文系统字体的环境中稳定导出，使用公开的 [Noto CJK](https://github.com/notofonts/noto-cjk) 简体中文 Regular/Bold 字形子集，SIL Open Font License见 [Noto-OFL.txt](source/assets/Noto-OFL.txt)，原文件与子集身份见 [fonts-manifest.json](source/assets/fonts-manifest.json)。这是有公开再分发许可的原型依赖，不是客户字体、SDK或生产素材。图标为本包源码内绘制的简单线性 SVG。所有示例地址、编号、任务和连接状态均为合成演示。
