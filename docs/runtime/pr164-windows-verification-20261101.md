# PR164 Windows 补证

2026-11-01。产品及修正测试提交 `04d0af59741dcc7d285185f2d156bd640ac0f846`，整合 main `b154512a`（PR163），未引入 PR161/165。原 Linux NOT_RUN 记录保持形成时身份，修改前索引/报告原字节见 [归档](../archive/pre-pr164-windows-20261101/manifest.json)。

新增原生夹具首次构建发现 CS0119：局部 `Key(ushort)` 遮蔽 WPF Key 枚举。仅将比较中的枚举完整限定为 `System.Windows.Input.Key.Enter`；未削弱测试或改变生产逻辑。

在保留未修复产品控件的基线上，同一 Windows SendInput 回归实际提交 A=`000123450001` 后又提交 A+B=`000123450001000123450002`，原生负向失败。修复产品的完整 WPF Rebuild 零警告错误，逐键回归19项通过；既有 `--workstation-barcode-ready` 同样通过，包含 standard/compact、草稿/选区/焦点、Conflict、离线/待配置。

原生夹具经过 HWND 和实际 WPF 输入事件，SQLite 与内置 RCS/loopback HTTP 真正执行。检查 A/B 独立命令/作业/报文字节、前导零、旧结果选择、手动光标/删除、额外回车、在途输入拒绝、Pending/丢失回执沿同命令核对、Unknown 不重发。仅修正测试编译问题，产品修改仍限原 PR 的视图输入责任。

失败记录全部保留：一次在 Rebuild 尚未结束时误启动旧基线 DLL，该结果不能用于判断修复产品；随后完整构建后的第一轮因前台/焦点条件未满足而在首字符前拒绝，未改代码的重跑通过。各轮日志分开，不删除失败或将其改记 PASS。原始合成日志、源码身份和哈希见 [summary.json](evidence/pr164-windows-20261101/summary.json)。

同时复核 PR163：关闭专项、资源风险、原图、离线激活、窗口生命周期替代夹具和 MVS 87项合成检查通过。继承的 acquisition-lifecycle 测试旅程缺口及其原超时记录保持，不将未运行后半段计为通过。

本轮未操作当前客户端/真实设备，物理 PDA、真实 SDK、客户 RCS、安装/现场均 NOT_RUN。CI保持停用；D/RV不变。通过本次必要原生回归后，可按正常PR保护合并，原PR159的条码拼接审查项已有补证，另一文档归档审查项不在本包关闭。
