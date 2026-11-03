# 供包员主页与维护诊断 OPERATOR-WORKSTATION-01

2026-10-27。范围与替代关系见[工作包](../work-packages/WP-IPC-OPERATOR-WORKSTATION-01.md)。基于 main `d0a063137da19ba9e5dbfeaaec7a0bee326628e8`；不修改并行统一版本工作，不代表新安装包、真实设备或现场验收。用户原截图不入库。

## 当前行为

- 相机/RCS 明确延后配置、没有当前包裹且所有位置均不可准入时，作业页显示缺项数、缺项名称和“去参数设置”。已有包裹、未知原请求、单侧仍可供包或快照失效时继续显示作业事实，不能用引导遮盖原责任。
- 普通供包员看到位置状态、当前条码、任务结果/大字格口、任务字段和下一步。主页不显示相机地址、原始标识或技术展开区；普通身份不显示诊断入口。管理模式仍使用同一个 Application、同一个运行实例和缓存参数表单。
- 诊断按左相机、右相机、RCS 分别显示已知原因、状态时间和对应参数入口。未知原因明确写未确认，不能从 Unknown 推断供电/网络的具体故障；模拟连接明确标注模拟。
- 重连没有已配置的相机/RCS、未暂停、快照失效、忙态或仍有正在请求的作业时禁用，并显示原因。请求仍交由 Application 重新检查授权、资源和原请求责任；只使用当前运行配置，保持暂停，不重发未知原请求。
- 配置生效核对、备份恢复核对和待核对作业收进“其他维护操作”。完整相机读码内容按需展开；技术身份、原状态、配置修订、请求归属通过“复制诊断信息”获取。复制仅写本机剪贴板，不发送给任何人。
- 所有诊断内容共用一层页面滚动。主页取消原嵌套恢复滚动区；需要继续未尝试阶段时，检查说明和原动作守卫保留。全局暂停/恢复回执仍独立显示，诊断自身操作反馈也保留。

## 实现路径

`src/Ipc.Application.Sportmaster.Desktop/OperatorWorkspacePresentation.cs` 是只读呈现和重连前置提示；`SupplyStationWindow.OperatorWorkspace.cs` 实现缺项引导与有管理模式保护的参数定位；`SupplyStationWindow.Diagnostics.cs` 实现原因和动作分组；`SupplyStationPosition.xaml` 保留日常三组内容、当前结果及必要恢复动作。业务状态和连接机制没有在 UI 另建副本。

`tests/Ipc.Desktop.UiVerification/OperatorWorkspace.cs` 覆盖缺项/普通身份/尺寸/反馈/定位/原请求保留；已加入 `scripts/verify-commercial-wpf.ps1 -OperatorWorkspace` 和同源 Windows CI 原生验证列表。旧参数和视觉测试的布局断言随本次结构调整；焦点、草稿、原请求和历史查询断言保留，异步导航等待实际完成。

## 运行与证据边界

所有文件及临时数据使用 D 盘。以下命令在本包工作树运行，打开独立合成 WPF 窗口，不连接现场设备：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:RestoreLockedMode=true
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD --operator-workspace
```

正常结果来自实际 Application 和合成端口；未配置/读码失败/重复/RCS 未响应截图包含明确的显示投影。独立 `--task-hierarchy-chain` 通过真实 Application、合成读码及本机回环 RCS 验证业务链，不能把显示投影当作真实接口通过。截图产物目录由 `.artifacts/WINDOWS-WPF-EVIDENCE.txt` 返回；每次运行独立保存，不覆盖旧证据。实际 Windows 缩放为 125%，客户区覆盖 1366×768 和 800×420 逻辑像素。

V2 静态页面、字段和动作映射、全部基线图已核对。内置浏览器拒绝打开本地原型，因此未做该 HTML 的交互验收；未绕过协议限制。原生验证使用 HWND/Dispatcher，不用网页检测替代 WPF 验收。

验证结果：六组原生回归共 316 项通过、0 项失败；构建、源码 publish 与入口环境报告冒烟通过。阶段、原始日志和修正过程见[本包验证记录](evidence/operator-workstation-01/README.md)。publish 的关键 UI/业务程序集与测试程序集逐字节一致；不代表安装版现场启动。

## 现场验证仍未执行

尚未安排真实供包员、维护人员或真实设备。需要 2–3 名供包员和 1 名维护人员，在获准的现场运行环境完成正常供包、未读到条码、重复包裹、RCS 无响应四类任务，记录首次判断、是否找得到下一步、是否误投/重复上件、是否需要提示和处理耗时。另让维护人员从待配置进入对应参数并解释重连禁用原因。区分新包裹重扫和未知原请求处理，遵守当前 RCS 超时合同及现场安全流程。

现场观察结果再决定是否调整，当前只证明实现行为和限定模拟验证；D/RV 与历史验收状态不因本包改变。
