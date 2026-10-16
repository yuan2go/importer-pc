# 供包工作站 · UI/UX高保真交付 v1.1

**Canva可编辑画板（18页）：** https://www.canva.com/d/p5j7f4rRG_XUXQu

**只读预览：** https://www.canva.com/d/W_MeiRWrI3uvY9p

## 建议打开顺序

打开`供包工作站_高保真交互原型.html`查看交互；打开`界面图册.html`查看34张独立高分辨率PNG；评审和开发请阅读`docs/供包工作站_UIUX设计与开发交接说明.md`。

Canva和`design/供包工作站_可编辑UI画板_v1.1.pptx`为原生文本、形状与SVG画板，不是大图背景。二者为静态设计载体，不具备HTML的状态切换。导入时可能发生字体替代；像素基线以同版HTML和PNG为准。不附加或分发字体文件。

## 核心文件

|位置|内容|
|---|---|
|供包工作站_高保真交互原型.html|自包含离线版，44个可重复进入的评审状态|
|prototype/|同版可编辑HTML/CSS/JavaScript源文件|
|screens/|正常、相机离线、结果待核对、拒绝、无码、多码、拆包、少货、追溯、设置、恢复等独立PNG；含7种客户区布局|
|design/|18页可编辑PPTX、元素坐标源JSON、设计token JSON/CSS|
|docs/供包工作站_UIUX设计与开发交接说明.md|PAGE/REQ映射、数据/状态来源、按钮命令、权限、视觉规则、Windows控件条件映射、未决与验收边界|
|evidence/|28项检查结果、原型静态/降级渲染检查、资料哈希、Canva实际导入结果、示例导出、原生PPTX结构检查|
|tools/|本次Linux/Chromium验证与画板构建脚本；不是生产软件依赖|

## 操作样例

默认右侧“手动发车”展示待发送→处理中→WCS接受，数量36→48件；这是D-03不足量人工分支的评审候选。底部切换到“超时·结果待核对”时只允许查看证据，模拟接受只能从明确评审工具注入。拆包示例为包装30、保留18、移除12；扫描方向仍待D-04批准。登录仅使用演示账号`operator`及任意非空示例密码。扫码箱只使用`TC-2026-0091`。

**禁止输入真实凭据或用于控制设备。** 本原型没有SDK/WCS请求、数据库、持久化恢复、真实授权、实体联锁或生产安装程序。数据存在页面内存，重置场景或刷新恢复示例。原型完整运行无CDN/网络依赖。

本环境`file://`直接导航被浏览器策略拦截；实际通过Playwright向Chromium注入同一份打包HTML完成渲染与交互检查。未声称已在Windows双击打开、触控、真实DPI、真机SDK或WCS联调。目标Windows现场应再次验收。具体方法与失败/通过记录在`evidence/`。

## 技术复核脚本

可选依赖：Python的Playwright、可用Chromium、Node.js与pptxgenjs。脚本默认使用本轮环境的`/usr/bin/chromium`，移到其他系统需替换为当地浏览器路径；打开交互原型本身不需要这些依赖。

`python tools/preview.py`重新打包并截图；`python tools/verify.py`重跑原型风险检查；`python tools/export_boards.py`导出原生画板对象；`node tools/build_boards.js`生成PPTX。原HTML降级复核脚本须显式传入原文件：`python tools/source_review.py --source-html 原供包台.html`。

资料中的9张外链图未取得，原HTML只能在降级替换依赖条件下检查交互，不宣称原版像素一致。D-01～D-16未决项详见开发交接说明。本次视觉完成不等于上述产品/硬件/接口问题已经批准。
