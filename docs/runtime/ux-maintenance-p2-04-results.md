# 三项维护P2修复结果 · 04

本次在main `be7956447054054dfdcfce725e8fbbbdfc87407a`之上修复全任务复审发现的三个P2，独立分支 `codex/ux-maintenance-p2-04`。设计先行提交1d76b62，实现19c2fda，原生关闭及控件反例补差ab8f4d4。继承已合并PR37/39；没有自动合并PR、改变历史证据或放宽Unknown/CAS守卫。

|问题 / WP-03映射|本次源码闭合|验证边界|
|---|---|---|
|关窗静默丢草稿 / 用户加强要求|主窗排空后检查全部所属窗口，辅助窗只检查自己；当前来源原始编辑指纹及原生绑定/Validation保护，默认取消、显式放弃；取消保选择/原CAS，停止失败仍保内存草稿。关闭代次阻止迟到子窗绕过主窗取消，Dispatcher让Closing先退出再最终Close。|38条窗口控制反例、63条配置/列表检查；原生转换错误/未提交文本/明确放弃的测试源码与交叉构建完成，Windows执行NOT_RUN。|
|参数页签返回空表 / UX-03、UXC-03|reader与库存分别维护所选实例/提供者订阅，刷新只作用本表；页面/上下文生命周期退订与重新同步。|真实WPF验证器加入往返、另表提供者更换和默认/只读反例；原生执行NOT_RUN，不能据源码宣告实机通过。|
|合法长列表被单项长度截断 / UX-03、UXC-03|string-list按既有schema的数量和序列化总长度提供多行JSON数组输入，明确单项/数量说明；原Value不转换，不丢空项或转义。|合法长列表及非法JSON/超项/超长由原schema反例验证；WPF原生完整输入/只读断言已补，原生NOT_RUN。|

草稿基线覆盖schema1/2/3及高级JSON；仅成功PendingRestart回执接受提交时基线，保存中继续编辑仍脏，拒绝/冲突不接受基线。旧schema切高级JSON使用当前候选，避免转换时掉旧版表单修改。不自动保存、不引入第二配置账或跨会话明文草稿仓库。没有改变Application流程、原请求/目标/合同/回执、SQL/存储和真实驱动能力。

## 验证与身份

[本轮证据](evidence/ux-maintenance-p2-04/README.md)记录准确源码字节、命令、日志SHA及负证据。macOS arm64/.NET10.0.402，仅合成参考输入；配置CAS/恢复、窗口资源生命周期、Busy/Pause与两位共享实例投影定向通过，三业务WPF和原生验证工程交叉构建0警告/0错误。两轴[独立复核](evidence/ux-maintenance-p2-04/independent-review.md)未发现确定新增缺陷；不把静态复核记作原生执行。

本次沿原WP-06准备新不可变候选，业务1.3.3/Contracts1.9.0/DB7；新精确参考能力2.2.5、reader1.3.5、RCS1.1.5按冻结宿主重建，最终产物及发布回执另记。原公开参考Release是[6ce1d6d](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-6ce1d6d)，它不包含本次修复。新发布须由package-application.py冻结新的完整Git输入，按目标公共DLL重新取得精确扩展/新不可变身份并执行同版旅程；旧ZIP不作为修复后产物或本轮同版冒烟证据。

## 复验与剩余

```sh
dotnet run --project tests/Ipc.Application.Verification -- . --sportmaster-window-lifecycle
dotnet run --project tests/Ipc.Application.Verification -- . --ux-commercial-configuration
dotnet run --project tests/Ipc.Application.Verification -- . --ux-commercial-presentation
dotnet run --project tests/Ipc.Application.Verification -- . --sportmaster-ui
# Windows x64，按既有机器策略/账户配置；本轮尚未原生执行
dotnet run --project tests/Ipc.Desktop.UiVerification -- .
```

Windows原生控件/默认取消焦点、DPI/键盘/触摸、普通账户安装更新恢复及目标负载仍NOT_RUN；真实SDK/协议与权威Unknown处置BLOCKED_PER_CAPABILITY，商业输入账仍待确认，Production NOT_READY。WP-01/04/05/06原门禁不因修复三项P2关闭，WP-07新增可选建议仍单列。没有授予现场资格或D/RV批准。
