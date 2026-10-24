# 供包台字体与排版 · TYPOGRAPHY-01

2026-10-24 工作包工程规范，适用于 Windows 11 原生 WPF 两套宿主、Sportmaster、参数、查询、管理/登录、保存核对、安装与首次配置。本规范覆盖旧当前指引中冲突的字体、字重及字号；PARAM-UX-02/V3、管理模式和 HIERARCHY-02 的导航、字段归属、空态及业务守卫继续有效。原型和历史截图不重制，清理前字节见[归档](../archive/pre-typography-01/README.md)。

本轮追加要求优先：Noto 与下列尺度先作为候选，完成原生对照后保留为当前工程选择，不以“用户已决定字体”代替工程判断。旧实现/验证保留 `c993418` 身份，现行选择由[对照方案与验收条件](typography-comparison-01.md)和[本次结果](../runtime/evidence/typography-01/controlled-review/README.md)共同约束；不推定 Codex 使用的具体字体。

## 字体及交付

采用 Noto Sans SC 对应的官方简体中文桌面静态 OTF：Noto Sans CJK SC 2.004，Regular 400 / Medium 500。只使用这两个实际字重，不用大面积粗体或合成半粗体。字体来自 [Noto 官方仓库](https://github.com/notofonts/noto-cjk/tree/Sans2.004/Sans/OTF/SimplifiedChinese)，固定 Sans2.004 提交；保留原字节与 SIL OFL 1.1，来源/版本/覆盖/哈希随字体清单记录。不是设计 WOFF 子集，不依赖在线下载或全局安装。桌面、安装和首配使用同一资源。

主字体后按 Microsoft YaHei UI、Segoe UI 回退；逐字符核对的条码、任务标识、技术原文使用 Consolas（系统字体，不随包复制），中文再回到界面字体。普通可复制中文、状态、原因、数量不默认等宽。采用 WPF pack 资源和实际家族名，参照 [Microsoft 字体打包说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/packaging-fonts-with-applications)。

本轮用户授权官方 OFL 字体进入仓库及离线交付，只限定替代“生产只用系统字体/不交付字体”。客户原稿、私链、生产数据、凭据、未获许可字体/SDK仍禁止上传；OFL不授予项目或其他资产许可。

## 语义尺度（DIP）

| 信息角色 | 尺寸 | 字重与布局 |
|---|---:|---|
| 正文、输入、导航、普通按钮、记录正文 | 16 | Regular；单行输入至少40，关键动作至少44 |
| 标签、说明、表头 | 14 | Regular；分组/选中项可用Medium |
| 低频技术元数据 | 12–14 | 按需展开；不承担当前结果 |
| 分组标题 / 作业位名称 / 页面标题 | 16 / 18 / ≤20 | Medium；查询标题保持16 |
| 正常状态 / 等待占位 / 异常主句 | 16 / 18 / 20 | 正常与占位Regular，异常Medium |
| 当前真实条码，标准 / 紧凑 | 28 / 24 | Consolas Regular；长内容换行、滚动、可选择复制 |
| 当前有效短格口，标准 / 紧凑 | 64 / 44 | Medium；长格口按空间减小，可完整查看复制 |
| 波次、任务号、SKU / 日志原文 | 16 / 14起 | 标识/原文等宽，详情可放大 |

有效格口或需要处理的异常最高，其次为当前条码。固定标题、正常设备和辅助信息降低强调。Unknown、Rejected、Conflict、失效和未准入的现行区别不变，只有当前有效结果使用大号格口。隐藏的空任务字段不恢复。低高度以局部滚动和重排保留完整结果行、查询范围、操作回执及原请求责任；不通过缩小全局字号或清空草稿解决空间不足。

## 实现与验证边界

共享语义资源同时供 XAML 和动态 C# 控件引用，覆盖模板与 SizeChanged；保持虚拟化、焦点、选区和滚动。不提供字体配置平台，不变更协议、设备或业务架构。必要原生证据记录源码、客户区DIP、系统DPI、实际字体URI/字重和截图；模拟窗口、实际系统缩放、真实设备与现场分别记录。验证与运行命令见[工作包](../work-packages/WP-IPC-TYPOGRAPHY-01.md)。

实际共享实现为 `src/Ipc.Presentation.Wpf/{UiTypography,TypographyScale}.cs`，两套宿主及安装/首配均引用此程序集。已核验每个字重65,535字形、44,810条Unicode映射、静态字库、fsType=0；完整覆盖清单不代表任意外部日志都只含这些字符，缺字仍走系统回退。版本以官方name表2.004为准，WPF的GlyphTypeface.Version在这组OTF上返回0，不作为版本来源。实际字体URI、资源哈希与原生对照见[实现证据](../runtime/typography-01.md)。
