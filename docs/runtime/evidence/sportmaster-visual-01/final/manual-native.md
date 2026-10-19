# 同版真实窗口输入检查

2026-10-20（Asia/Shanghai），运行源码dfc2ab68d263340037751651c442dd5b33023b32的冻结win-x64介质；DLL和库存见上级MEDIA.json。通过`verify-commercial-wpf.ps1 -VisualProductization -Interactive`启动同版原生夹具，显式合成身份和参考端口；真实WPF/Dispatcher/窗口、实际应用和SQLite，不操作真实设备或网络服务，不配置机器政策。

使用computer-use skill的Windows窗口API选中本次验证进程。最初1366×768 DIP窗口超出本机可见工作区，先用原生系统菜单最大化后再检查；只保存已确认可见的目标窗口。主机WPF DPI为1.25，报告工作区1536×816 DIP，工具截图输出1536×816；这不是1440×900目标屏或其他缩放的验收。

|实际输入|观察|
|---|---|
|激活本次工作台，Alt+Space → X最大化|标题、导航、状态、左右位、固定动作和页脚全部在可见区；格口是主要结果，工程材料折叠|
|鼠标点击左位条码，Ctrl+A|UIA焦点为`CurrentBarcode`，SelectedText为`000123456789`；下一次读码健康时间刷新时仍保持选择|
|鼠标进入作业记录|顶部选中态切换，原查询4条合成记录和所选原件详情保持；完整请求/原目标/合同/报文摘要可查|
|鼠标进入设备与维护|未保存原因`SYNTHETIC unsaved settings`保留，认证/保存和低频草稿操作分层；没有因为导航重建Application|
|返回工作台|两位原作业/原格口继续有效，状态读取持续推进|
|展示设置 → 标准/紧凑布局|紧凑变为同位横向条码/格口，顶部导航收为当前页面入口，来源行隐藏；实际重排，不按整窗缩放|

截图：[标准工作台](visual/visible-hwnd-workbench-125.jpg)、[记录](visual/visible-hwnd-history-125.jpg)、[维护](visual/visible-hwnd-maintenance-125.jpg)、[紧凑工作台](visual/visible-hwnd-compact-125.jpg)。截图都是本次同版合成窗口，没有其他应用、窗口清单、秘密、生产数据或客户原稿。

异步、异常、长内容和首配步骤由同一交互预览之前的原生夹具逐项执行并捕获，见visual/WINDOWS-WPF.log及after PNG；手动输入仅覆盖上表，不把其他程序化调用写成人工点击。预览保持八分钟期间定时器继续运行，随后由夹具设置合成草稿关闭回调并释放窗口；此回调不进入发布客户端。

未验证：物理触控、手套、HID实物、读屏、声音、现场视距、混合显示器DPI、100/150%、目标Windows安装/UAC/第二账户、真实SDK/WCS与现场实体。软件暂停不是安全急停；Production、现场和商业验收状态不变。
