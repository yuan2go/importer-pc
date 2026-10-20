# main 续核证据

Method: design review only (detector not applicable, native platform)。Impeccable Operate 原生审查；网页/CDP/机械扫描不适用。ui-ux-pro-max 独立 WPF 查询得到键盘导航、视图职责及命令绑定；错误恢复查询用于行内下一步提示，网页 aria 示例未直接套用 WPF。

本轮 context 启动器退出0，但初始命名目标路径不存在，不能将其输出称为实际 XAML 检测结果。随后直接核对实际 `Ipc.Application.Sportmaster.Desktop/SupplyStation*.xaml`、共享页面/日期控件、首配与主题、实物基线和当前原生截图，以已提交项目设计和 WPF 适配为权威；不新增平行 PRODUCT/DESIGN。

## 四轮判断

critique：四类页面完整继承，双位格口是主要结果；真正遗漏是非法日期被日历关闭恢复为旧条件（P1，已用旧冻结 DLL 原生复现）。layout：固定左右与动作、局部滚动和按任务首配保持，错误提示在原日期字段下自然换行。clarify：明确“无效、尚未应用”及重新填写/选择/清除三条恢复方式，避免把空白日历误当不限范围。polish：使用原生 DatePicker、原系统字体和语义控件；同码/原请求、焦点/选择/草稿、紧凑与辅助窗未倒退。已查看本轮实际错误、首配、工作台、维护和最小客户区图；不因设计评分授予商业资格。

|Nielsen 项|分值 / 4|本轮结论与边界|
|---|---|---|
|状态可见|3|原作业结果、读取时间及下一步持续；现场新鲜度仍需真实合同|
|符合操作语言|3|中文作业与有效格口层级明确，精确身份保留在诊断|
|控制与退出|3|草稿关闭拒绝、显式清除与暂停；实体处置不由 UI 猜测|
|一致性|3|首配/工作台/维护/帮助沿原控件语言，低频维护仍有较密参数页|
|错误预防|4|本轮日期风险及原检查/保存/原请求守卫由原生断言核验|
|识别而非记忆|3|四步首配与搜索帮助可发现，专业诊断按需展开|
|效率|2|刷新和导航稳定；实体键盘/HID/触控全矩阵未验|
|视觉克制|3|格口突出、中性专业信息层级；最小客户区依赖局部滚动|
|错误恢复|3|非法日期保留错误及恢复路径，Production 不伪造成功|
|帮助|3|离线任务搜索与本地支持预览齐全，真实支持合同待填|
|合计|30 / 40|Good；单一上下文工程设计判断，不是独立现场评分|

角色检查：首次配置者能循四步并保存未完成输入；持续作业者在轮询/切页后保留上下文；支持人员遇到非法范围能明确修正，不能用旧时间误导导出。未验证的物理输入、可访问性和现场能力分别留界，不补造人物或批准。

## 证据身份

冻结源码 `1744f398720b405448d975ab9d440cd24feeef74`，[MEDIA.json](MEDIA.json)含包/库存和实际引用 DLL 路径。原生范围为 Windows22631 / 125% / 显式合成身份与端口。`commercial-pages/` 24项，`visual/` 30项；两个 RESULT 的来源及库存相同，全部原断言保留。PNG 是真实 WPF RenderTargetBitmap 布局捕获，不是新人工点击或目标屏幕验收。

- [原失败复现](before-date-repro.log)使用未修改的 dfc2ab6 介质共享控件，配套 `.cs.txt` / `.csproj.txt` 为原临时夹具归档；不是新客户端入口。
- [历史日期错误](visual/after-history-invalid-date-1100.png)、[支持拒绝](visual/after-support-invalid-date.png)展示保留无效范围与明确修正方法。
- [工作台](visual/after-normal-1366.png)、[首配](visual/after-first-configuration.png)、[维护](visual/after-maintenance-1100.png)、[最小客户区](visual/after-minimum-client-800x420.png)为同版视觉旅程。
- [整理助手修正](custody-helper-correction.json)保留误将不同 TFM 第三方资产要求字节相同的初次失败；现核验固定夹具引用来源，未改产品断言。

`checks.json`校验原始机器记录与所选图片；Git manifest 另校验本目录全部提交。执行 `python docs/runtime/evidence/sportmaster-visual-01/main-closure-01/verify.py --media D:/sm-visual-closure-media-01`（提交前加 `--staged`），复核冻结输入、历史不变、变更路径/清单/证据及实际介质。不是全树历史归档 PASS 或批准签名验证。

目标 Windows 安装/UAC/第二账户/重启/完整迁移、额外/混合 DPI、触控/HID/读屏/声音/视距/长时负载 NOT_RUN；SDK/合同/设备/实体、签名及现场 BLOCKED。原 D/RV 与历史资格不变。
