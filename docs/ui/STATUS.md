> 当前组合：业务1.2.6；classic/supply-station共用快照时效、当前位身份及维护保护。继承PR34 c073044的已复核实现，并补退役运行实例拒绝、修订倒退失效、旧格口标旧及轮询失败时独立软件暂停。原实物、原SHA/PASS/FAIL与实际Windows NOT_RUN保持，执行来源见[审查03工作包](../work-packages/WP-IPC-COMMERCIAL-AUDIT-03-DELIVERY.md)。

# UI实现与实物状态

Sportmaster当前WPF按left/right显示各自reader、作业、条码、格口及主链/后台责任，当前位命令与历史Owner分别选择；共享Application及连接。维护页已有完整schema3 JSON读取、校验、保存及加载时Revision CAS，旧单Reader表单仍只读保护拓扑。草稿、Pending、已确认Effective、尚待确认的当前运行输入和ActivationFailed分别显示。维护页“运行输入确认”读取原配置账、原字节SHA256、读取时运行实例/配置修订及当前输入；确认绑定读取时三段Owner，自动刷新不推进该确认身份，也不覆盖未保存配置草稿。旧账资源启动成功仍须暂停/维护认证及检查说明，确认后仍保留软件暂停。异常恢复页列原接收发生及OriginalRawMissing/关联Query；Query只证明结果，原Awaiting及原图、归档、包资料责任保留。DirectPlc完整UI按已批准延期；不新增Sportmaster展图或长期图库。

来源为[v1.1实物与完整交接](ss-ui-vis-001-v1.1/README.md)，原HTML/CSS/JS及34张同源PNG保持原身份。新WPF是Application业务实现，不能据此声称旧HTML/RV已修复。实际Windows截图0；双窗、DPI/HID、声音/触控及安装交互NOT_RUN。跨平台真实VM/CAS检查与WPF交叉构建分别报告。[N01前原字节](../archive/pre-architecture-quality-02/README.md)。

## 原始资料与历史验收

2026-10-16：依据用户明确要求，新增 [最终完整交付v1.1实物](ss-ui-vis-001-v1.1/README.md)。原HTML、分离源码、18页PPTX/JSON、tokens和完整UI交接已保存；34张PNG由同源重新导出，不是历史PNG或WPF截图。

旧状态中的“没有对应完整原型/未入库”对本组已被本次实物入库取代。历史获取、未决事项及原资产问题仍保留，不倒写成通过。

## 入库前历史状态（原文保留）

# UI资料状态

当前公开保留的既有文件：`tokens/tokens.json`、`tokens/tokens.css`，来源SS-UI-VIS-001 v1.1，保持原字节。

它们是工程设计建议/参考变量，不是客户确定的设备规格，也不是已通过验收的布局。`minimumEffectiveClientArea`、字号、控件尺寸等必须结合RV-13与现场验证评估。JSON中的`source`为历史出处字符串；此处没有对应完整原型，不把它解释为可访问的资源路径。

完整高保真单文件HTML、分离HTML/CSS/JS、可编辑PPTX、画板JSON、34张页面/状态/布局截图、组件页和历史验证均已收集到非公开包。因公开范围未明确，没有在此以空文件或截图摘要替代。续作重新只读核验两份Canva元数据并分段保存页面文字；当前v1.1云端更新时刻已变化，仍没有新的完整可编辑导出，也没有修改云端设计。

字体名称只是CSS字体栈。本仓库不包含、不分发任何字体文件、SDK或驱动。

原PRD九图、原产品HTML七个CDN文件已在续作取得，新增本地离线阅读副本与历史降级副本分别保留。原v1.1 HTML/CSS/JS及业务逻辑未修复；旧28项检查/44场景和最小窗口假设仅属历史有限证据。PPTX原件18页、画板18页不证明最新云端身份或跨渲染器验收。

本轮包内资源检查另发现原PPTX的265个.png部件实际为SVG字节，已按部件/哈希登记格式不一致，原件未修。它们不计为可解码位图；SVG内部资源继续检查。此归档发现不等于RV-17修复或对当前云端/Windows的验收结论。

## DELIVERY-12实现增量（历史）

受控组件与部署维护页、首次Loaded前应用暂停/多窗条件修复见 [12映射](extension-delivery-12-mapping.md) 和 [逐项报告](../runtime/extension-windows-delivery-12.md)。源码与核心策略验证已执行；本包真实WPF截图0，Windows UI/缩放/输入/持续旅程NOT_RUN。原v1.1资产不变，历史HTML导出不是本包Windows证据。
