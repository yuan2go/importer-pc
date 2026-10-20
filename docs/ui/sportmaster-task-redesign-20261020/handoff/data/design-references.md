# Windows 供包台重设计：适用参考与视觉判断

核查日期：2026-10-20。以下为设计参考，不构成对现有 importer-pc 的实现或交付验收。原型可以借鉴 Windows 交互原则，在现有 WPF 中实现；不要求迁移 WinUI，也不直接照搬文档中的 API。

## 采纳的五项一手参考

| 来源 | 本次采纳的规则 | 在供包台中的落点 | 不直接照搬的部分 |
| --- | --- | --- | --- |
| [Microsoft — Forms](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/forms) | 将相关输入形成表单；区分即时更新与显式提交；选择适当输入控件；错误在字段附近显示；保留清楚标签与键盘顺序。 | 显示偏好可以即时改变；相机/WCS 参数采用显式保存并显示生效状态；日期用日期控件，设备用实例选择器，错误定位原字段。 | 文档示例的 80px 单字段总高、24px/48px 间距并非本工控机的硬约束；不把一次保存等同于安全应用。 |
| [Microsoft — Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) | 对话框首句说明问题；按钮使用具体动作；保留安全退出；字段错误不弹阻断对话框。 | 未保存离开时用“继续编辑 / 放弃本次修改 / 保存后离开”；应用设备参数时说明影响对象与重连需求；重复扫码和普通成功不弹窗。 | 不照搬 ContentDialog/XamlRoot API；WPF 使用现有窗口和命令实现同一交互语义。 |
| [Microsoft — Progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls) | 反馈要说明正在执行的任务；能估计进度时显示确定进度，不能时显示不确定进度；只阻断真正受影响的交互。 | “正在校验左相机参数”“正在保存工作站配置”“等待本次包裹结束后重连左相机”；连接测试逐层显示结果。 | 不编造百分比、剩余时长或连接测试层级成功；不因一次测试而锁住整个主窗口。 |
| [Microsoft — Develop accessible Windows apps](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility) | 控件应暴露名称、角色和值；关键操作可用键盘；支持高对比；普通文字对比至少 4.5:1，大文字至少 3:1。 | 左/右、成功/超时/断线使用文字加图形而不只靠颜色；Tab 顺序沿任务走；焦点和选中有明确边界；生产实现要验证 UI Automation。 | 浏览器原型不能证明 Windows Narrator、系统高对比或 DPI 已通过。 |
| [Microsoft — Alignment, margin, and padding](https://learn.microsoft.com/en-us/windows/apps/develop/ui/alignment-margin-padding) | 主要内容无需缩放才能读清；固定关键区域，其他区域弹性布局；统一对齐、间距和样式资源；以 4 为基准建立尺寸体系。 | 左右结果区稳定，不因状态文案长短交换位置；参数对象栏、标题和保存条保持稳定；使用共用 ResourceDictionary 统一控件。 | 文档中的 epx 数值不是截图像素；本设计的尺寸、字体和断点必须经过目标工控机验证。 |

## 明确属于设计假设的数值

截图的像素尺寸不等于应用的 DIP 或物理显示器尺寸。部分图名与像素宽度呈 1.25 倍关系，只能提示可能有 125% 缩放，不能确定所有截图的真实 DPI。建议主原型按约 1600 × 900 的逻辑画布设计，并额外走查 1366 × 768 级别窗口。准确目标需由现场屏幕、系统缩放、观看距离和输入方式确认。

以下是可采纳的初始 token，不是工业标准规定值：正文 14–16 DIP；组标题 18–20 DIP；当前格口 72–104 DIP；包裹号 20–26 DIP；表单输入框 36–40 DIP；行高 40–44 DIP；按钮 36–40 DIP，触摸模式候选为 44–48 DIP；页面边距 24 DIP；相关字段间距 12–16 DIP；组间距 24 DIP；圆角 4–6 DIP。不要同时追求所有小窗口全信息可见：小窗口优先保住左右结果、包裹与明确异常，历史与技术详情可滚动或收起。

配色建议：浅灰背景 #F4F6F8、白色工作面 #FFFFFF、正文 #1F2937、次要文字 #5B6472、操作蓝 #155CB4、成功绿 #187047、注意色 #9A5900、故障红 #B42318、边框 #CBD2D9。正常状态的结果可用深色大字；绿用于结果已验证标记，避免把整页染绿。警告不使用只有颜色的浅黄色字。

## 本次不采用的模板与样式

- 营销网页的超大标题、英雄区、装饰图、渐变光效、悬浮卡片与大面积留白，没有帮助现场完成扫码或配置。
- 通用后台的多层菜单、每对象徽章与统计卡，会增加状态判断负担；不因有参数就建立复杂后台平台。
- 大型流程控制室的全厂拓扑、模拟仪表和高密度报警墙，与双路供包台的核心任务不匹配。
- “skill 建议了某种审美”不是采用依据。当前任务应依据对象归属、错误恢复和连续阅读检验设计，视觉规范只提供可重复的质量约束。

## 字体检查与可用来源

已实际执行字体文件搜索：`/usr/share/fonts`、`/usr/local/share/fonts`、`/opt/codex/runtimes/codex-primary-runtime`。找到 DejaVu、URW、LibreOffice 附带的 Latin/Arabic 等 Noto 字体，未找到中文 CJK 字体；`fc-match ':lang=zh-cn'` 回落到 DejaVu Sans，因此当前环境不能仅靠系统 fallback 保证中文清晰。

Windows 生产字体建议优先“Microsoft YaHei UI / 微软雅黑”，数字采用稳定等宽数字样式；渲染环境可使用官方 Noto Sans SC / Noto Sans CJK SC。

- [Google Fonts — Noto Sans SC](https://fonts.google.com/noto/specimen/Noto%2BSans%2BSC)
- [Noto 官方下载说明](https://github.com/notofonts/noto-cjk/blob/main/Sans/README.md)
- [Noto 官方仓库](https://github.com/notofonts/noto-cjk)

官方说明支持按语言与字重下载，并区分 language-specific 与 region-specific subset。可从该说明进入 Simplified Chinese 的 OTF 或 TTF 链接。根据父代理后续授权，已从官方 raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/ 下载 Regular 与 Bold OTF 到 assets，并保存对应 SIL OFL 1.1 为 assets/Noto-OFL.txt。最终原型内嵌或打包字体时应同时保留该许可证。

## 父代理可直接复核的工具引用

- Forms：turn2view0，已打开全文。
- Dialogs：turn1view0，已打开全文。
- Progress：turn2view1，已打开全文。
- Accessibility：turn5view2，已打开全文；turn4search7 官方搜索命中。
- Alignment：turn5view1，已打开全文；turn4search0 官方搜索命中。
- Font download guide：turn5view0，已打开全文；turn3search3 官方搜索命中。

最终答复如果使用这些 ref，应由父代理自行 open 对应网页后再引用。文件中的来源均给出可直接打开的网页 URL。
