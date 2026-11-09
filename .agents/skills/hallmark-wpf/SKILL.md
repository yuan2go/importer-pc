---
name: hallmark-wpf
description: "Review, simplify, or refine importer-pc native WPF UI using a Hallmark-derived, industrial-desktop workflow. Use when Hallmark is named or when reviewing workstation navigation, state cards, dialogs, typography, density, or interaction. Default to read-only audit; change code only when the user requests implementation. Not for marketing pages or a new UI framework."
---

# Hallmark-WPF · importer-pc

这是项目本地的设计审查方法适配，不是上游 Hallmark 的完整安装，也不是新组件库。先读根目录 [AGENTS.md](../../../AGENTS.md)、[当前文档索引](../../../docs/INDEX.md) 和 [WPF 审查方法](../../../docs/design/wpf-ui-review.md)。现行产品规则由 AGENTS.md 定义；本 skill 不另立业务或视觉标准。

## 选择模式

- `audit <范围>`：默认模式。只审查，不改项目文件；给出证据、影响、最轻的有效整改及验证方法。用户另有保存报告要求时只写指定报告。
- `refine <范围>`：用户明确要求落实修改时使用。在现有设计和组件归属内整改；先说明文件范围和保留项，再执行，不把小改扩大成全站重设计。
- `study <参考>`：从用户提供且可用的参考提取层次、密度和交互原则；不复制品牌、字体或素材，不改项目。参考不能证明当前程序已经符合要求。

这些是本地工作模式，不是上游 CLI 参数。未明确范围时采用当前用户指向的页面或控件；材料不足先完成可证实的源码审查，标明缺口，不重复询问已知偏好。

## 执行顺序

1. **读现状。** 记录基线提交和工作区改动；不替用户清理、暂存或覆盖已有修改。读目标 XAML、动态 C# UI、绑定/命令与相关现行文档。沿实际资源合并和调用链确认生效位置，不能只看一个未被加载的模板。当前只涉及 SM 及其必需共享基座，不扩展暂停开发的 Application。
2. **留基线。** 记录目标区域现有的内容、状态、输入、命令、焦点、窗口归属及受影响文件。截图标明版本、场景、DPI 和客户区 DIP；生成图仅是设计意图。缺少 Windows 环境时明确“原生未运行”，不拿 HTML 或静态检查代替。
3. **审查并分级。** 先判断操作员是否能辨认当前状态、原因和下一步，再查层次、密度、对齐、克制、一致性及键盘操作。区分“已验证事实 / 工程判断 / 待验证”。业务与可访问性缺陷不能用美观豁免；审美偏好不能自动升格为发布阻断。
4. **限定整改。** `audit` 和 `study` 到此停止。`refine` 只修授权范围内的问题，保留业务命令守卫、协议、配置和数据结构；不因缩小控件删除焦点、命中区域或语义。不为通过检查恢复废弃逻辑，也不删除有效业务断言。
5. **对照验证。** 对比同版本来源可追溯、同尺寸、同场景的前后原生截图，逐项核对保留清单，运行与改动风险匹配的既有验证。静态差异、构建成功和截图相似都不能单独证明交互正确。复审还要说明旧版有什么做得更好。
6. **交接。** 用方法文档中的短报告结构交付。改动走开发分支和 PR，不强推、不直接改 main；不刷新发布 manifest，不生成安装包。没有独立审查人时写“自审”，不伪称独立验收。

## 本项目适配边界

- 使用现有 `Workstation.xaml` 主题键及 `UiTypography` / `TypographyScale`，遵守当前字体规范；不导入上游网页字体、调色盘、CSS、主题抽签、Hero、大数字看板或页面差异化要求。
- 简约靠减少无用容器、重复提示和错误强调，不靠缩小必要正文、隐藏异常责任或删掉处理方向。正常待机、离线、已停用可以共享构图，不能合并其业务状态。
- RCS 离线不能解释成条码错误；相机离线不能禁止合法人工输入；小车提示不能被 UI 新增为准入守卫。已确认结果、业务拒绝、处理中、Unknown 与停用中按现行应用状态投影，不被通用空态覆盖。
- 导航和窗口按现行规则：窄图标栏保留说明、键盘焦点和可点击区域；退出默认取消，Esc/× 是取消确认，不是执行退出；继续退出仍走草稿保护及正常停止。不要把这些规则错误套用到全屏主窗口的 Esc。
- 只做已授权的隔离/合成验证，不控制现场设备、不向真实 RCS 发试验请求。用户截图、生产日志、条码和配置不因审查而自动上传或进入公开仓库。
- 本 skill 不自动调用其他设计 skill、下载 engine 或安装 Node/浏览器依赖。需要查 WPF/UX 资料时再按需使用已有 UI UX Pro Max 或 Impeccable；它们的建议不能覆盖现行规则。遇到旧路径/旧规则冲突先标明，不借此恢复废弃设计。

来源、固定版本、保留与排除项见 [UPSTREAM.json](UPSTREAM.json)；上游许可见 [LICENSE](LICENSE)。升级必须显式审阅差异，不能自动跟随上游 main。
