# 统一资料入口

工作包：WP-IPC-DESIGN-BASELINE-01（续作）；核验日期：2026-10-16。

## 当前范围

**PUBLIC_SUBSET_ONLY / 公开许可阻塞，部分完成。** main 只包含已公开的问题表、通用 tokens、归档管理文件和必要校验工具。完整 PRD、产品 HTML、设计/开发/UI 正文、PPTX、画板、图像、评审全文及来源映射仍在 Git 外受限本地包；没有获授权私有远端，不宣称全文已在本仓库 main。

## 阅读顺序

1. [公开范围与逐类处置](baseline/PUBLICATION.md)、[缺失/未上传](baseline/MISSING.md)。
2. [未关闭问题与证据边界](reviews/STATUS.md)、[完整17项历史问题表](reviews/issues.json)。
3. [冲突和待决策项](baseline/CONFLICTS.md)、[变更记录](baseline/CHANGELOG.md)。
4. [UI状态](ui/STATUS.md)、[变量JSON](ui/tokens/tokens.json)、[CSS变量](ui/tokens/tokens.css)。
5. [同一工作包及关闭标准](work-packages/WP-IPC-DESIGN-BASELINE-01.md)、[逐文件清单](manifest.json)、[校验用法](../tools/README.md)、[本轮检查范围与结果](baseline/continuation-checks.json)。

## 本轮取得与证据更新

指定输入 ZIP 的 SHA-256 匹配，193 个文件通过 CRC/路径安全检查，原 manifest 的 191 个记录逐项匹配；另两项是 manifest 及其校验文件。这些数字只用于辨认输入。

九张 PRD 外链图与七个原 HTML 依赖本轮已从原引用取得。此前获取失败保留为历史事实，不继续当作本轮缺失。原稿与降级副本原字节均保留，新增离线阅读副本只定位资源，不修复业务。

两份 Canva 设计的元数据及分段页面文字已在本地保存；当前云端更新时刻与旧记录不同，未取得新的完整可编辑导出，不能将本地旧 PPTX 当作最新云端设计。历史截图引用还有34个确定路径未随输入交付；已在受限包逐项登记，不能用新图替换。

## 后续读取约束

受限包按原始资料→独立评审→设计/开发→UI读取；S/T证据指向哈希匹配的原稿和历史文件，原行号不转指阅读副本。原要求、HTML模拟行为、工程建议、未批准规则和历史验证分别解释。旧设计“唯一主定义”和旧UI检查通过均不能覆盖未决项、后续评审或缺失证据。

当前入口不冻结客户业务规则。数量拆包、实体入场/复位、双相机映射、真实 WCS/SDK、已有 C# 工程、Windows 与现场验收仍未补齐；后续优先级见评审状态。
