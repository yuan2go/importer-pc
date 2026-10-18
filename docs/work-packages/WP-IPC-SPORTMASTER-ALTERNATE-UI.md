# WP-IPC-SPORTMASTER-ALTERNATE-UI

用户2026-10-18批准第二套Sportmaster原生WPF UI、客户端参数选择、必要验证后提交推送并创建main PR，不合并；允许衍生WPF代码公开，原HTML不上传。[设计](../superpowers/specs/2026-10-18-sportmaster-alternate-ui-design.md)。

## 实施计划

使用writing-plans / executing-plans，在已建立的隔离工作区顺序执行。

- [x] 1. 参数与投影：新增 `DesktopLaunchOptions.Parse(string[] args, string applicationId)`，返回BusinessLaunch与Ui；缺值/重复/未知/跨业务先拒绝。新增SupplyStationViewModel及位置投影，链接真实源文件到验证工程。
- [x] 2. WPF接线：新增SupplyStationWindow及Position控件、主题；Presentation按参数选择窗口；主窗拥有Stop回调，辅助窗共享Application；复用历史与维护页面。原型不支持的业务按钮不进入运行路径。
- [x] 3. 定向验证：执行 `dotnet run --project tests/Ipc.Application.Verification -- <repo> --sportmaster-ui`。断言 `Parse([], "sportmaster").Ui == "classic"`，两位同码不同实体/原请求不混合，VM共享真实RuntimeId，旧schema无伪双位，Unknown不冒充格口取得，刷新过期不开放命令。
- [x] 4. 构建与回归：必要Sportmaster/DirectPlc/WcsToPlc WPF编译；既有 `--application-architecture` 受影响验证。固定dotnet SDK10.0.402，构建 `-m:1 -nr:false`。
- [x] 5. 发布：先提交冻结源代码及随包指南，再用 `scripts/package-application.py --application sportmaster --target win-x64` 生成实际WPF候选及portable参考包；核对解包hash、两套资源、参数说明与真实控制台启动冒烟。
- [ ] 6. 同步报告、manifest、Windows后验，保留原证据；提交推送、创建并读回main PR，不合并。

## 文件职责

`src/Ipc.Desktop/DesktopLaunchOptions.cs`拥有桌面参数政策；`ParcelApp.xaml.cs`预校验和窗口装配；`Sportmaster.Desktop/SupplyStationViewModel.cs`仅投影及命令转发；`SupplyStationWindow`拥有导航/刷新/窗口生命周期；`SupplyStationPosition`拥有每位展示；`Presentation.cs`选择窗口。不得修改业务Flow、ParcelRuntime、SQL、驱动或外部协议。

验证使用既有真实Application入口及合成数据，不引入另一业务实现。新增测试必须覆盖参数失败与位置/实体/版本边界；Windows窗口/焦点/DPI/触屏实际NOT_RUN，真实SDK/WCS/现场BLOCKED，交叉编译/HTML截图不代替运行证据。D/RV及历史报告保持。
