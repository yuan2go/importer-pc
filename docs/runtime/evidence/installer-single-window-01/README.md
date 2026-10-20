# 单窗口安装实际验证 · 2026-10-20

最终冻结源码 `27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`，分支 `codex/installer-single-window`，Sportmaster/SM业务模块v1.4.1。交付身份、实际EXE/ZIP校验与边界见 [delivery.json](delivery.json)。新证据记录发生在冻结构建之后，不改变EXE源码身份；最终运行结果未冒充已写入先前冻结的随包指南。原7c/129包和证据保持。

## 实际Windows结果

[RESULT.json](RESULT.json)、[E2E.log](E2E.log)为本次原始捕获字节：58项PASS。在普通用户启动实际单EXE后，先测试准备时一次关闭取消且清理自己的私有目录、不改原登记；再在现有隔离Simulation部署上实际升级。自动准备直接进入同一安装选项窗口，可浏览程序目录、设置独立数据目录、选择两种快捷方式，返回保留输入；上述阶段均无UAC。点击安装时真实Windows请求一次授权，由用户亲自点击“是”。原计划预检、完整备份、配置/数据保留、受保护登记和源码清单均核对通过。完成进入实时双位工作台，两个真实快捷方式分别启动正常工作台并退出0。

[MONITOR.json](MONITOR.json)记录1043次采样，名义间隔20ms：一个持续可见安装HWND、一次新consent进程、零隐藏采样、零新增可见控制台窗口。没有声称检测到小于采样间隔的所有闪动。截图只捕获被测自己的窗口：[准备](actual-preparation.png)、[工作台](actual-workstation.png)、[保守托管路径工作台](actual-conservative-workstation.png)。目录/摘要/完成截图和INSTALLATION.json含本机路径，仅本地保存，未公开；没有截取其他应用或UAC安全桌面。

[原始探针](Probe.cs.txt)及[工程](Probe.csproj.txt)在运行前保存，实际fixture SHA见delivery.json。工程为net10.0-windows/WPF/Windows UIAutomation，原生PrintWindow仅截图自己的窗口。编译：`dotnet build Probe.csproj --no-restore -m:1 -nr:false`。执行：`dotnet Probe.dll <实际EXE绝对路径> <新证据目录> 27b0d8f6cc0281fd82ee68fcda350233cb0a45ff --upgrade`。此命令会实际升级已存在且属于该探针的隔离Simulation登记、创建快捷方式并请求真实Windows授权；不是只读检查，不要对生产登记运行。初次安装、干净系统不在本次58项范围。

## 兼容性与相关检查

实际OS为Windows11 Home23H2/build22631，AMD Ryzen5 3550H，.NET10.0.13。只读诊断[正常](NORMAL-ENVIRONMENT.json)/[关闭优化](CONSERVATIVE-ENVIRONMENT.json)均退出0、未创建工作台或UAC；关闭托管intrinsic与ReadyToRun后再次启动实际安装的Simulation工作台并读取同进程诊断。该检查仍在相同物理CPU，不能当作无AVX2CPU、IntelCPU、Windows10或厂商原生SDK验证。

[payload-verification.json](payload-verification.json)与[检查程序](payload-verification.py.txt)核对最终实际介质：27个应用托管DLL均无ReadyToRun NativeHeader，x64宿主/CLR/SQLite、WinExe helper、嵌入符号、BUILD-INFO/清单和随包指南一致。包装器另外逐字节核对全部共享Ipc DLL，并要求输出恰好一个EXE。不是仅检查项目属性。

聚焦测试：payload28项、包装器7项、原升级/Unknown/计划变化/完整备份8项、快捷方式18项（隔离COM）、兼容策略12项（合成OS输入）、统一介质14项、指南2项通过。相关log为UTF-8/LF公开摘录，只删除本机EvidenceRoot行；原始log SHA及此前完整套件授权失败、实验封装失败见[日志保管与失败](attempts-and-log-custody.json)。原失败没有换成PASS，完整commercial-productization未通过，本轮没有更改其授权守卫。最终原生安装使用原生产预检/备份机制而非测试伪造。

Windows10 22H2 Home/Pro、其他实际CPU、旧无AVX2实体、干净受支持系统、海康SDK/驱动/相机、WCS/RCS及现场验收仍NOT_RUN。本机系统厂商支持周期与本次功能运行结果分开，详见[兼容报告](../../windows-client-compatibility-01.md)。EXE未Authenticode签名，未捆绑无再分发授权的海康SDK/驱动；不宣布Production资格或关闭旧D/RV。

## 文件身份

本目录`.gitattributes`保持原证据字节。`evidence-sha256.json`覆盖本目录除自身外全部文件；Git总manifest另记录规范Git blob。变换过的测试摘录和失败摘录明确标识，不冒充原始字节。
