> 2026-10-30追加纠偏以[人工供包与调试10](sm-manual-debug-flow-10.md)为准：键盘/PDA供包不要求相机在线或已配置；真实本位业务编码、RCS、暂停/隔离及原请求守卫保留。本文形成时的相机依赖条件和验证只保留历史范围，不改写原证据。

# 作业导航与条码输入 · SM-NAVIGATION-INPUT-09

2026-10-30，基准main `9b1384d5`，独立分支`codex/navigation-input-fix-01`。工作包见[09](../work-packages/WP-IPC-SM-NAVIGATION-INPUT-09.md)。

截图中的设置/日志来自03把四个入口常显、点击再认证的实现；调试仍在设置的内部工具区。现恢复普通模式只显示作业/记录，管理模式显示设置/日志/调试；宽侧栏与紧凑页面菜单一致。调试使用已有`StationDebugControl`，退出/到期沿既有撤权逻辑回作业，不新建采集或发送器。

左右位只保留一个条码显示/输入框，任务信息固定在下方，提交放底部。移除独立置顶输入区和两位未配置时的全屏配置引导。相机离线/待配置不隐藏输入及任务。图片通过查看图片按钮打开原观察/作业；输入来源和清空位于条码右键菜单，基准冲突后才显示核对当前位。

界面区分自动显示与手工编辑：未编辑时跟随当前读取结果，编辑后保留草稿/选区/焦点并提示任务对应原条码。刷新不把草稿写回业务事实；仅显示的相机结果不能当人工输入重复提交。键盘/PDA回车和按钮共用既有Application命令，前导零、过期基准、防重及未确认提交核对不变。离线或配置不足仍按原准入守卫拒绝，不伪造设备/业务成功。任务字段保持只读。

低高度布局把条码与图片入口同排，回执纳入本位纵向滚动区，保留800×420客户区的条码/当前格口与主要操作。其余内容纵向滚动，不缩小规范字号。

Windows原生WPF定向验证59条PASS，既有operator-workspace回归30条PASS，均使用独立D盘合成Simulation端口/参考对端。覆盖普通/管理模式两种导航、宽/窄菜单一致、直达调试与撤权返回、条码输入/图片入口、前导零/键盘/PDA回车、防重复、刷新保焦点/选区/草稿、相机更新后的基准Conflict及原请求字节保持。离线/待配置为显式只读快照投影，不通过投影提交业务；真实准入命令使用原Application。

1366×768、1024×768、800×420客户区，Windows实际DPI125%；原生截图已查看。低高度格口曾被回执及头部挤出，几何断言保留并修复布局后通过，不放宽断言。首次导航测试遇到折叠菜单未生成视觉项，改为读取真实Menu.Items后通过；中间失败不冒称PASS。UI验证及SM Desktop构建均0警告/0错误。

[证据与源码字节清单](evidence/sm-navigation-input-09/README.md)记录同轮来源。以上不继承03～08历史PASS，不扩张为真实相机/PDA/客户RCS、安装、目标工控机或现场验收。未发布新安装介质或替换现用安装；D/RV保持原状态。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/ipc-navigation-input-fix-01 --workstation-barcode-ready
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/ipc-navigation-input-fix-01 --operator-workspace
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false --nologo --verbosity quiet
```
