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

实际交付为[不可变参考预发布1.3.3](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-ff31b0e)，产品冻结 `ff31b0efaaebd7e5fae4254c7184e0640e2eea81`，UI功能实现ab8f4d4。业务1.3.3 / Contracts1.9.0 / DB7，新精确参考能力2.2.5、reader1.3.5、RCS1.1.5按冻结公共DLL构建并签名。五ZIP包括三个portable core及Sportmaster selected portable/win-x64，另有三IPCX/身份、公钥参考信任、原始证据ZIP和hash账；私钥未上传。原6ce1d6d的1.3.2不含本次修复，保持不覆盖。

[ARTIFACTS](evidence/ux-maintenance-p2-04/ARTIFACTS.json)记录产品身份；[远端回读](evidence/ux-maintenance-p2-04/remote-publication.json)绑定实际资产。五ZIP CRC/文件hash/冻结SHA及Windows PE身份、三个core解包主链、签名选装/CAS/Pending激活/启动失败保旧/安全退休、RCS原报文与raw回执/重启不重POST均通过。四组定向回归、C#/Python版本组合和冻结源三业务WPF/原生验证器交叉构建通过。这里的参考执行不代表Windows原生或现场执行。

产品冻结后的交付说明、证据和manifest有后续提交；[source-parity](evidence/ux-maintenance-p2-04/source-parity.json)核生产/配置/脚本/示例/随包指南与产品冻结逐字节相同。原始CombinationRun/事件及旅程在Release raw ZIP；公开替换路径副本另列Raw/Published hash。资料检查268→267项：修复本次manifest校验文件名格式，267项设计原件继承错误仍FAIL，新增0项，不扩大例外。

开工main仍be79564。并行PR41的Windows账户预检不属于本包，候选不含该PR；业务源码无重叠，共用INDEX/manifest/Windows指南若后续整合须显式保留双方来源。本次正常推送并创建[main PR #42](https://github.com/yuan2go/importer-pc/pull/42)，保持OPEN，不自动合并；[回执](evidence/ux-maintenance-p2-04/delivery-receipt.json)分别登记PR head、main、产品冻结和实际Release。

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


获取候选后先核SHA256SUMS，portable解包运行 `dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-reference-root>`；Windows按包内application.ps1、SPORTMASTER-UI.md和完整Windows后验说明使用 `--ui classic` 或 `--ui supply-station`。参考签名信任为TestOnly，不连接未授权设备。
