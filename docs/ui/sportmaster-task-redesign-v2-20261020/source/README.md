# V2 离线原型源文件

这是可编辑的设计模拟，用来表达任务、参数归属、状态和界面。正式客户端继续使用当前 C#/WPF；这些 JavaScript 内存结构、模拟认证、计时、导出、重启和恢复都不能替代生产合同。

## 文件职责

| 文件 | 职责 |
|---|---|
| model.js | 示例状态、四类候选、依赖校验、配置账投影、原请求冻结、分位准入。 |
| views.js | 固定左右作业框、三组信息、持续状态摘要、通用控件。 |
| views-config.js | 系统/业务模块/驱动/组件表单、对象树、差异、校验及生效状态。 |
| startup.js | 首次启动独立五步表单与检查/启动反馈。 |
| aux.js | 记录、诊断、备份/模板/迁移、帮助支持和安装衔接的模拟交互。 |
| app.js | 输入、焦点与滚动保留，命令和异步阶段，保存/未知/恢复等交互。 |
| scenarios.js / scenes.json | 60 个明确标注的审阅场景；场景切换会重置模拟状态。 |
| styles.css | 本版视觉规格、布局、状态、焦点及有限空间策略。 |
| assets/ | 内嵌中文字体子集、许可和既有品牌资产。 |
| build_prototype.py | 仅用 Python 标准库生成单文件 Prototype.html。 |
| render_screens.cjs | 使用本地 Playwright/Chromium 导出67张 PNG与几何记录；拦截外联。 |
| build_gallery.py | 从 screens.json 生成静态 Gallery.html，并更新 README 的逐图索引。 |

## 构建与打开

在仓库根目录执行：

```bash
python3 docs/ui/sportmaster-task-redesign-v2-20261020/source/build_prototype.py
```

然后直接用浏览器打开上一层 `Prototype.html`。无需 npm、Web 服务、CDN、登录或现场连接。浏览器内容安全策略禁止 `connect-src`；原型里所有成功、失败和维护动作都有模拟说明。

需要重新导出时，使用环境中已安装的 Playwright 与 Chromium：

```bash
node docs/ui/sportmaster-task-redesign-v2-20261020/source/render_screens.cjs
python3 docs/ui/sportmaster-task-redesign-v2-20261020/source/build_gallery.py
```

如 Chromium 不在 Playwright 默认位置，可设置 `PROTOTYPE_BROWSER` 为已安装浏览器的可执行路径。重导出会更新 `screens/`、`screens.json`、`qa/render-results.json`；不要把新的输出冒认为旧验证回执，更新版本和实际检查范围。

## 使用与限制

- 推荐用1600×900逻辑客户区审阅；常规PNG为2倍分辨率。1366×768、1280×720和800×420只是浏览器逻辑空间检查，不是Windows缩放验收。
- 最底部“原型演示”栏是审阅工具，不进入正式产品；用它注入下一包或选择特定状态。
- 首次配置只作为首次启动流程。普通导航“参数配置”对应后续维护。
- 相机/RCS连接检查只模拟层级与结果，绝不调用示例API。没有权威查询能力的分支准确说明未支持。
- 日期选择器可能按本机浏览器区域设置显示月/日/年或12小时制。正式WPF应按约定中文日期和24小时格式统一显示，并在目标Windows验证。
- 原型的字号/空间折叠为设计表达。最终主操作命中区域、Windows原生控件/字体、UI线程响应和焦点守卫按工作包实测收口。
- 字体子集按当前中文生成。增改文案需核查字形覆盖，保留OFL许可；生产优先既有Windows系统字体。
