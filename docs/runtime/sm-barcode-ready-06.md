> 2026-10-30追加纠偏以[人工供包与调试10](sm-manual-debug-flow-10.md)为准：键盘/PDA供包不要求相机在线或已配置；真实本位业务编码、RCS、暂停/隔离及原请求守卫保留。本文形成时的相机依赖条件和验证只保留历史范围，不改写原证据。

> 2026-10-30当前导航与单一条码输入以[SM-NAVIGATION-INPUT-09](sm-navigation-input-09.md)为准：普通模式只显示作业/记录，管理模式增加设置/日志/调试；离线/未配置仍保留左右条码和任务区，直接输入条码，底部提交。本文形成时独立置顶输入/四项入口常显说明保留历史范围，既有验证不转记到09。

# 相机就绪时的作业条码输入 · SM-BARCODE-READY-06

2026-10-30，源码父提交c6fc8f41558c6aeccb187e43c515d88caa244027；main 9df3901e已包含PR157，分支已整合并保留05修复。

此前输入框已支持就绪时补录，但排在空的结果区之后，空态仍偏向等待相机。现在把同一控件提前到相机状态下方，提示键盘/PDA回车提交；结果为空时收起只读图片区，任务空态显示“等待扫码或输入条码”。读到条码后恢复本次条码/图片入口，输入区继续保留。沿用现行字体、44DIP控件和相机/扫描/任务分组。

生产源码仅改SupplyStationPosition.xaml和SupplyStationViewModel.cs布局/文案。提交、CAS、前导零、单命令核对、管理隔离、筛选、准入、发送及MVS/原图所有权保持，没有绕过连接或原请求守卫。

## 验证

真实Windows WPF配合独立D盘合成Simulation端口/参考对端，15条PASS断言，退出0：左右就绪可编辑；五次快照保草稿、选区、焦点且不自动提交；键盘提交与PDA回车进入实际Application，前导零保留、重复回车不多发；已取得格口后图片入口可用；相机更新当前包裹后旧草稿提交Conflict且保留草稿、无额外请求。

1366×768、1024×768、800×420客户区的原生截图已查看，DPI125%；小窗输入保留在纵向滚动区，未出现页面横向滚动。WPF验证工程、Sportmaster Desktop构建均0警告/0错误。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc-ui-debug --workstation-barcode-ready
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false --nologo --verbosity quiet
```

[证据](evidence/sm-barcode-ready-06)包含日志、源码字节清单及实际WPF合成截图。参考格口和条码不是现场结果；未附运行数据、账号或客户图片。未改现用安装或调用真实相机/PDA/客户RCS，04整轮和现场验收仍待完成。
