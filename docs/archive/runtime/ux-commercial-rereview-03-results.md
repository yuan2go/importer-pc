# UX闭环02 · 独立复审后三项续修

本次修复1a34a1e复审确认的三项工程差额，**不声明WP-01～07整体商用闭环**。原PDF、PR37、f1a655c参考发布及原验证结论保留；当前实现与原生/真实验收分别读取。

## 源码及产物身份

- main仍`eeeed0e0700cd07a76587b2bb701e91d422b2c35`；PR37依赖`2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94`，远端未合并。本次更新[PR39](https://github.com/yuan2go/importer-pc/pull/39)，不合并任何PR。
- 文档先行`19dadc7`；功能修复`203c1a8`；最终源码/指南/版本冻结`6ce1d6d0b109bd6a3d9490fac176122b4670b90b`。业务1.3.2、Application.Contracts1.9.0、DB7、配置schema1/2/3；无新API/SQL或生产依赖。
- 精确参考包：capabilities2.2.4、reader1.3.4、RCS1.1.4，针对冻结Release公共DLL重新构建/签名；TestOnly信任只用于Simulation。
- [参考预发布](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-6ce1d6d)及[ARTIFACTS](evidence/ux-commercial-rereview-03/ARTIFACTS.json)记录实际5个产品ZIP、3个ipcx、独立identity/trust及原始证据ZIP。旧f1a655c资产不覆盖。
- 203c1a8和7036488的本地尝试因版本测试/模板同步不完整未发布；原失败和原本地文件保持。不能用其成功的产品冒烟掩盖版本检查失败，亦不把它们叫远端发行。

## 三项差额

|原映射|实施与检查|仍待验|
|---|---|---|
|WP-03/05、UX-04/07、UXC-04：紧凑入口|classic不再整体隐藏包含操作员登录/登出、会话与设备来源的诊断Expander；compact默认折叠，可展开并在原128 DIP局部滚动。原登录契约、独立维护认证和Pause保持。|Windows每个客户区展开入口、密码焦点、实际登录/登出、滚动与固定Pause；收起后工作台完整几何。|
|WP-03、UX-04/05：完成反馈|真实绑定Feedback在无新消息的成功只读动作后保留带操作名的完成说明；消息修订区分同文新回执，重复Busy入口不执行/不污染结果，独立Pause结果及当前读取失败保持优先。成功恢复读取可清自己的错误并显示新完成。|真实WPF绑定、原生Dispatcher与键盘操作后验。|
|WP-03/05：验证器|检查实际MaintenanceScope、reader表格、保存控件禁用及独立Pause；finally释放并await挂起动作。补真正TextBlock完成反馈断言。原生夹具启用真实参考认证；支持区精确客户区尺寸、四个条码/格口控件数量及未决摘要边界均有断言。|原生执行NOT_RUN；交叉编译不代表这些断言已运行通过。|

A→B选择归属、双位隔离、原请求/Unknown守卫和关闭排空继续继承并在本次定向回归中验证；不新增登录业务规则、真实SDK或权威Unknown接口。

## 必要验证与负证据

[证据索引](evidence/ux-commercial-rereview-03/README.md)列出实际命令、日志及哈希。macOS arm64/.NET SDK10.0.402；输入均为合成参考、本机loopback，无生产设备。

最终源输入的六组定向入口为presentation、audit03-snapshots、sportmaster-ui、sportmaster-window-lifecycle、version-compatibility、ux-commercial-configuration；含实际Feedback反例、同文新回执、独立Pause、失败/恢复、慢快照/历史选择、防串Owner、双窗CAS、草稿与生命周期。原反馈RED、两个本地版本尝试失败和一次缺using的交叉构建失败保留，未倒写。

Python17项兼容及2项随包指南检查；三业务WPF与原生验证工程交叉构建；5ZIP CRC/逐文件hash/冻结SHA和Windows PE身份；同版三个portable解包主链及签名选装/配置激活/启动失败保旧/安全退休、RCS原报文回执与重启不重POST，分别记录结果。不以构建耗时充当性能测量。

独立双轴复核对1a34a1e…6ce1d6d均无新增确定缺陷，见[Standards / Spec原文](evidence/ux-commercial-rereview-03/independent-review.md)。同一Pillow环境干净归档为267项继承错误、新增0项，整体仍FAIL，未扩大例外。

公共可读日志是明确替换开发路径的衍生副本，RawSha256/PublishedSha256在log-custody.json；完整原始日志、CombinationRun记录/事件、执行脚本和失败尝试保存在Release的raw-verification-evidence.zip。重验原事件hash须使用原ZIP，不能对路径替换副本冒称原字节。

## 按包状态与运行

|工作包|当前状态|
|---|---|
|WP-01|商业输入仍待确认，见[原输入账](ux-commercial-combination-02.json)。|
|WP-02|选择/Owner参考证据有效，本次回归通过；两布局原生交错待验。|
|WP-03|本次反馈/入口/验证器源码差额已修，定向参考验证通过；原生控件与真实维护仍待验。|
|WP-04|真实SDK、正式协议及权威Unknown依据BLOCKED_PER_CAPABILITY，不造接口、不清Unknown。|
|WP-05|紧凑入口源码已修，原生检查完整保留并加强；Windows/DPI/HID/焦点/触摸与像素可读性NOT_RUN。|
|WP-06|新固定参考候选及参考旅程单独留证；普通账户/UAC/ACL、安装更新中断恢复/卸载保数据、目标负载与班次NOT_RUN，商业组合未批准。|
|WP-07|继承追溯与维护能力；新增筛选/导出/安装日志枚举仍可选，不算既定缺陷。|

下载对应ZIP并核SHA256SUMS，解包到新目录；portable执行`dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-reference-root>`。Windows使用包内application.ps1及SPORTMASTER-UI-WINDOWS.md，源码原生验证入口为`dotnet run --project tests/Ipc.Desktop.UiVerification -- .`，须按既有机器策略和账户/ACL配置；不连接未经授权设备。软件暂停不是安全急停。

main、PR head、冻结产品SHA和实际Release分别记录；只关闭本次有证据的源码问题，不据参考模拟关闭原D/RV或商业门禁。
