# Windows10/11与x64 CPU兼容

2026-10-20用户明确：现场大多为Windows10 22H2专业版/家庭版x64，另有Windows11，CPU种类多。本轮将Win10 build19045和Win11 x64作为优先功能兼容目标，不将CPU厂牌作为允许/拒绝依据，不把运行时安装条件当成全设备或现场资格。

## 基础程序的兼容措施

同一个win-x64自包含包用于目标系统。应用和安装器显式设置PublishAot=false、PublishReadyToRun=false，不按开发机CPU的AVX/AVX2生成应用专用机器码；通用IL由目标机.NET10 JIT决定可用指令。微软.NET10分支的[指令集设计](https://github.com/dotnet/runtime/blob/release/10.0/docs/design/coreclr/botr/vectors-and-intrinsics.md)描述SSE/SSE2默认基线、运行时能力检查与高级指令回退，不能把.NET11主分支的新基线误当成本包.NET10要求。随包微软运行时、SQLite及未捆绑的厂商原生库仍有各自架构/系统条件，不声称关闭应用AOT可以模拟另一种CPU或消除第三方要求。

安装页高级设置增加“运行环境（可复制）”，只读显示具体Windows build/Edition/DisplayVersion、OS和进程架构、CPU型号、逻辑处理器数、实际运行时及运行时启用的SSE2/AVX/AVX2。这里的intrinsic标志会受.NET环境开关影响，并非物理CPU能力完整清单，不用AVX2=false拒绝x64机器。注册表信息不可读取时明确Unknown/诊断，不把缺失型号当成功资格。

基础启动策略要求原生x64 OS/进程，Windows内核版本10.0 build14393起；Win10 22H2/build19045和Win11/build22000起标为本轮优先目标，其他满足基础条件的Win10版本明确另需验证；32位及ARM64不宣称支持。基础版本/架构检查不等于已验证可运行海康SDK、网络合同或生产业务。

已安装Desktop提供只读诊断入口：`Ipc.Desktop.exe --environment-report`，可由PowerShell管道接收JSON；不创建工作台、业务实例、设备连接、数据或UAC。正常双击流程不增加任何必答步骤。没有要求现场另装.NET SDK、Python、Git或开发工具。

## 微软支持和现场资格分开

[.NET10官方系统列表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)当前列出的Windows10为特定Enterprise/LTSC/IoT版本，不能将其泛化到所有Home/Pro22H2。Win10 Home/Pro常规支持已在2025-10-14结束，见[微软生命周期](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro)；ESU资格另行核对，不能因为购买ESU就自行声称已进入.NET10完整官方支持矩阵。本产品以用户明确的现场系统为功能适配目标，同时明确厂商支持风险和实际验证边界，不通过伪改版本/兼容模式或静默降级运行时假装全覆盖。

海康SDK/驱动须核对厂商对Win10/Win11具体版本、CPU和驱动的说明，当前精确SDK资格仍按[原驱动指南](hikrobot-reader-01.md)。未授权SDK再分发，不捆绑厂商安装器，不把普通软件启动当成真实读码通过。

## 本轮验证与剩余矩阵

纯策略覆盖Win10 22H2、Win11多build、其他Win10旧版、32位/ARM64及低于运行时基线的拒绝；这是合成输入检查。最终EXE另以实际安装和关闭托管硬件intrinsic/ReadyToRun的本机工作台启动验证通用托管路径，并读回同进程诊断标志；后者仍是同一台CPU，不能称作无AVX2实机、另一CPU、另一Windows或厂商原生库资格。

当前只有本机Windows11 Home23H2/build22631 x64，未找到可用Win10虚拟机或已连接现场电脑。Win10 22H2 Home/Pro、Intel/AMD及其他原生x64厂商各代表CPU、旧款无AVX2实体、海康SDK/驱动与读码、干净系统、现场验收仍为待实机项。未提供CPU型号时不编造已支持型号或最低性能规格。原本机系统过微软支持期的事实和旧D/RV保持，最终结果按各自精确commit记录。

## 最终冻结27b0d8f的实际证据

[最终原始记录](evidence/installer-single-window-01/README.md)为58项安装/启动PASS，其中实际运行时诊断与禁用托管intrinsic/R2R启动属于同机补充检查，非其他CPU资格。实际OS/CPU/运行时JSON、27个应用IL头检查、两个工作台原生截图和固定探针分别存档。Windows10 22H2与其他CPU仍未实际运行，不因纯策略12项PASS、关闭优化或本机安装通过而改为已验。本轮无可用Win10虚拟机/现场连接；尚需在用户现场系统上验安装/取消/启动/快捷方式、SDK与相机读码及业务接口。
