# 本轮原生合成证据

开工main f3059d499cd4dbc60b443f0a54cac939b2e5db2f。before/来自页面实现前的真实WPF运行，UI源为该main，使用本轮初始合成夹具；不是HTML、生成图或最终发布DLL。before/native.log保留当时状态/尺寸/125%DPI。基线夹具在工作区新增后执行，未冻结该次二进制；其范围只用于前后视觉比较，不赋予最终介质资格。

最终介质固定源码为dfc2ab68d263340037751651c442dd5b33023b32，库存hash、包身份及版本见[MEDIA.json](MEDIA.json)，同版夹具与逐项结果在final/。中间开发构建不作为最终发布身份。

本轮Impeccable Operate实际评审：

|阶段|判断与修改|验证方式|
|---|---|---|
|critique|P1：导航/工程身份抢占作业重心；P1：轮询全字段通知干扰持续操作|先看原稿与本机before画面，检查实际XAML与投影代码；原生Assessment A，网页B不适用|
|layout|固定左右结构；条码/有效格口/结果分层；标准纵向、紧凑横向；来源和技术材料按需展开|真实WPF代表尺寸，800×420最小客户区；发现并修正字段挤出/父容器裁切|
|clarify|中文状态、原观察与旧责任分开、原请求核对、下一步；原生日期/时间/显式UTC偏移|非本机+05:30、非法时间、清除范围；底层筛选值保持原契约|
|polish|统一表单/表格/菜单/展开区；首配步骤与低频操作；移除页签状态向正文继承颜色/字重|看正常/异常/长内容、记录/维护/首配/支持真实页面，验证焦点、选择、滚动、草稿与共享实例|

Questions skipped: 2 Priority Issues；用户已经指定实现范围和优先项，本轮按明确授权直接修复。没有将审美建议变成客户规则或Production资格。context启动器实际退出0，engine0.1.16；它未识别XAML视觉实现，故以项目原稿/真实WPF为准。首次缓存输出存在上游batch标签诊断但context实际返回；没有以缺识别结果宣称页面无问题。网页扫描/CDP、营销模板和动效不适用，不计PASS。

ui-ux-pro-max实际查询：`binding update focus selection --stack wpf`得到code-behind仅视图逻辑/进度/绑定3条；`error recovery next action --domain ux`得到错误恢复/可访问错误/字段错误3条；`form progressive disclosure --domain ux`得到提交反馈/表单标签/网页标题3条，其中网页标题规则未用于WPF。未运行design-system冒充WPF定稿。上游完整文件与LICENSE/NOTICE及本地适配见项目skills。

开发原始失败保留在development/：继承sportmaster-ui套件在解析检查后因本机没有维护安装的机器政策拒绝Start（不是PASS）；菜单移入原生Menu后旧夹具按Button搜索失败（已改为菜单树查找）；最小客户区字段被挤出且父容器裁切（已修复并增加实际字段/父边界检查）。不得删断言或把这些旧记录倒写。最终机器结果另列。

本轮只发布合成包裹/参考设备与无秘密的界面，不包含真实SDK、客户原稿、新生产数据、账户凭据、机器窗口清单或用户未跟踪Excel。继承55025ce证据仍按原SHA/原范围读取；本轮不关闭旧HTML/D/RV、不构成现场/商用验收。

整合增量：实施期间PR56合入main `1e97740f73fa1714505a128283f3df5bb96a5518`。最终组合完整继承其首配目录→设备→策略→检查启动四步及独立恢复页、未保存确认、检查失效/保存启动守卫、可搜索离线帮助、草稿和工程门禁修正与全部历史证据；本轮仅在其首配上补视觉分组/可复制诊断，未保留一套并行首配。原计划的三步组织由此替代，最终验证已重冻新组合，不能拼接旧PASS。PR57仍为独立发行任务。

## 最终证据的读法

- [首配/商业辅助页结果](final/commercial-pages/RESULT.json)及[原生日志](final/commercial-pages/WINDOWS-WPF.log)：PASS；包含未保存关闭拒绝、目录不可读禁止写、候选检查失效、草稿动作、可搜索帮助和共享Application。原9247夹具因按钮移入展开区而查找失败；保留原断言，先打开对应展开区后通过，旧失败在development/9247db6-stale-fixture/。
- [主工作台最终结果](final/visual/RESULT.json)及[逐项日志](final/visual/WINDOWS-WPF.log)：PASS；证明本次观察/原作业/原请求关联，没有用格口取得替代物理完成。首次dfc预览缺输出标记目录，全部业务/布局断言已过但整次结果仍FAIL；失败在development/dfc2ab6-preview-directory-failure/，脚本创建目录后同版重跑通过，八分钟交互预览后正常释放窗口。
- [同版实窗输入记录](final/manual-native.md)：真实HWND上的鼠标/键盘与可见区截图，独立于布局捕获。只发布合成目标窗口。
- [全树归档检查摘要](archive-audit-summary.json)：仍FAIL，267项均在继承原稿；新增内容身份/清单checksum无错。保持原稿历史，不设豁免冒报全树PASS。

布局图由同版实际WPF视图树RenderTargetBitmap捕获，DPI125%；1366×768、1100×650、900×520为窗口DIP，另有精确800×420客户区。超出本机工作区的布局图只能证明对应原生排版；实窗手动检查另在最大化可见区完成，不能拿布局图冒充该目标屏幕/缩放现场验收。

|比较/状态|原生图像|
|---|---|
|实施前主台|[before](before/before-normal-1366.png)|
|双位有效格口|[after](final/visual/after-normal-1366.png)|
|左处理中/右已取得|[异步](final/visual/after-left-processing-right-valid-1366.png)|
|Unknown与拒绝|[异常](final/visual/after-unknown-rejected-1100.png)|
|长内容/失效|[长内容](final/visual/after-long-content-1100.png)、[失效](final/visual/after-stale-1100.png)|
|最小实际客户区|[800×420](final/visual/after-minimum-client-800x420.png)|
|历史/维护|[历史](final/visual/after-history-1100.png)、[维护](final/visual/after-maintenance-1100.png)|
|首配/帮助|[首配](final/visual/after-first-configuration.png)、[帮助](final/visual/after-support-help.png)|

NOT_RUN：100/150%与混合多屏DPI、触控/手套/HID/读屏/现场视距、目标Windows26100+提权安装/UAC/第二账户、真实SDK/WCS/实体和长时负载。当前Windows22631本机125%合成界面检查不授予Production或商用验收资格。
