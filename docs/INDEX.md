# 统一资料入口

基线任务：WP-IPC-DESIGN-BASELINE-01；归档日期：2026-10-16。

## 当前范围

**PUBLIC_PARTIAL — 已收集完整可访问字节，但公开main只包含经过公开范围检查的子集。** 未公开的原稿、全文设计、HTML/PPTX、图片和历史核验证据保留在单独的非公开交付包。主仓库没有它们的空壳占位文件，也未先上传再删除。

## 阅读顺序

1. [公开范围与保留原则](baseline/PUBLICATION.md)、[缺失/未上传事项](baseline/MISSING.md)。
2. [未关闭问题与使用边界](reviews/STATUS.md)、[完整17项技术问题表](reviews/issues.json)。
3. [冲突和待决策项](baseline/CONFLICTS.md)、[基线变更记录](baseline/CHANGELOG.md)。
4. [通用设计变量JSON](ui/tokens/tokens.json)、[CSS变量](ui/tokens/tokens.css)；其来源和假设见[UI资料状态](ui/STATUS.md)。
5. [本次工作包及关闭标准](work-packages/WP-IPC-DESIGN-BASELINE-01.md)、[资料清单与哈希](manifest.json)。

## 原始资料与非公开整理范围

原始PRD 1.1、未标版本产品HTML、SS-DESIGN/DEV/UI 1.0、SS-UI-VIS 1.1、SS-REVIEW 1.0已取得。三个交付压缩包逐项展开；保存原稿和历史截图，不以交付摘要代替正文。完整目录/逐文件来源/SHA-256保存在非公开包的 `docs/manifest.json`。

当前详细设计与开发说明的读取顺序：原始资料→独立评审→详细设计→开发说明→UI输入/高保真。前次设计中的“唯一主定义”等措辞不能覆盖未决项和后续证据；原型验证通过也不能覆盖独立评审指出的缺陷。

这里的读取顺序是归档管理约束，不是客户确认任何新业务规则。没有修复关闭证据时，不发布“已修复版”或“生产就绪版”。

## 可开展与不可定型

可参考完整问题表开展问题分解、状态约束设计、通用组件和接口隔离准备；在取得授权需求正文前，不应凭此公开子集冻结客户业务实现。真实SDK、WCS语义、实体边界、数量规则、既有C#工程和现场验收均未在本次补齐。
