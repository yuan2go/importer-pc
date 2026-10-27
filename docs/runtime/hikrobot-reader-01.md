当前配置仅schema3，Parcel数据库仅10，Supply仅6/6；其他格式拒绝并提示使用全新数据目录。程序集版本与包身份以当前项目声明和本次构建清单为准。旧指南原字节及形成时证据索引见[收口前归档](../archive/pre-fresh-baseline-current-guides-01/README.md)，不用于当前操作。

当前驱动包装2.0.0保留bcr-code及SDK原生ABI，观察源仅走受控端口；单机分类、native停止所有权和帧守恒继续有效。真实SDK双机资格另验。

# 海康读码驱动 · 运行与验证

[本工作包](../work-packages/WP-IPC-HIKROBOT-READER-01.md) · [设计与边界](../design/hikrobot-reader-01.md)。本驱动独立于左右位，通过 `device.ip` 选择相机，业务拓扑仍由原配置绑定。本轮不是Production或现场放行。

## 实现与依赖

独立工程 [Ipc.Drivers.Hikrobot](../../src/Ipc.Drivers.Hikrobot/Ipc.Drivers.Hikrobot.csproj)，原生 ABI 声明与 SDK 接管在 NativeMethods.cs，观察、队列、健康和句柄生命周期在 HikrobotReader.cs；无WPF、SQLite、WCS、PLC依赖。唯一公共依赖为冻结宿主的 Ipc.Platform.dll。目标Windows x64 / .NET10，编译SDK10.0.402；厂商ABI边界固定MvCodeReader 2.0.0.4（原包SDK证据不扩张到当前包装2.0.0），仅GigE，版本不符拒绝。ABI事实记录在 [sdk-boundary.json](../../src/Ipc.Drivers.Hikrobot/sdk-boundary.json)，无第三方SDK正文或二进制。

Platform新增 [NativeSdkBoundary](../../src/Ipc.Platform/NativeSdkBoundary.cs)，原独立签名部署资格仍是本地SDK文件的批准权威；ParcelCapabilityFactory传递已验证的原资格。原ExtensionLoading构造入口保留，无资格仍拒绝原生SDK启动。SDK文件锁和已加载版本保持至进程退出，不能靠卸载托管扩展热换原生库。静态接受不等于安装、运行资格或现场批准。

当前Sportmaster业务3.0.0、Application.Contracts 3.0.0、海康包装2.0.0，精确组合见config/compatibility；没有新增Production资格。新扩展匹配本次宿主公共DLL，不改变原请求精确身份。

## 先做独立连接验证

从已构建的诊断目录运行（不启动任何业务/WCS）：

```powershell
.\Ipc.Drivers.Hikrobot.Probe.exe --list 'D:\InstalledVendorSdk\win64'
.\Ipc.Drivers.Hikrobot.Probe.exe --connect 'D:\InstalledVendorSdk\win64' 192.0.2.10 10
```

将SDK路径和文档示例IP替换为本机安装目录、实际相机IP；目录须包含MvCodeReaderCtrl.dll、MVGigEVision.dll、MvSmtDevCtrl.dll及其完整同位数依赖。本程序没有写IP、设置触发/曝光、软件触发、复位或外发指令。`--list`输出设备IP/型号/固件和序列号hash，仅供本地，不提交真实输出。`--connect`只接收并计数码，不输出/留存码内容；无帧时明确报告0，不伪造读码成功。诊断用临时物理锁只属于此独立进程工具，不能代替日常宿主机级租约/账户政策。

源码构建（PowerShell；命令中的绝对路径替换为实际路径）：

```powershell
dotnet build src/Ipc.Assembly/Ipc.Assembly.csproj -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true
dotnet build tests/Ipc.Drivers.Hikrobot.Probe/Ipc.Drivers.Hikrobot.Probe.csproj -p:IpcContractPath=D:/FrozenHost/Ipc.Platform.dll
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Debug/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --self-test
```

独立Probe的可发布版本支持 `-r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json`，包含.NET运行时，不包含厂商SDK；不能把该工具当成Production启动器。

## 接入现有Application

1. 选择同版冻结宿主，按原安装/维护流程准备指定账户、机级租约、独立信任和只读扩展Store。不要修改当前生产配置或在活动原请求上切换实现。
2. 原 `scripts/build-extension.py` 使用 `--definition src/Ipc.Drivers.Hikrobot/extension.build.json --version 2.0.0 --contract <冻结宿主/Ipc.Platform.dll> --check-host <冻结宿主/Ipc.Assembly.dll> --output <新ipcx路径> --key <Git外私钥> --publisher <受信发布者> --trust <独立信任>`。源码须已提交且干净。此声明为Simulation组合，真实相机输入仍为SDK来源；不是合成数据，也不是Production资格。生产构建用 `extension.build.production.json`，必须有独立非TestOnly发布者和完整真实组合批准；本轮未生成生产签名或批准。
3. 按原签名安装流程安装包。在现有维护页选择 `hikrobot.idmvs-reader/idmvs-reader`，实例填 `device.ip`；可选 `device.serial` 锁定预期序列号。驱动不含左右逻辑；原Reader的 `Driver=extension:<实例Id>`、`DeviceId` 与实例一致，由原schema3拓扑绑定本业务位置。单台设备的实际序列号机级租约防止多个别名双占。
4. 默认 `entity.mode=observe-only`，向Application报告 `UncorrelatedRead`，保留码观察但不创建业务作业。只有取得一触发一实体、同触发多帧、计数器重置和重连后原实体处置证据，才显式选择 `trigger`；设备本来须已TriggerMode=On。UTF-8/ASCII为显式严格编码，不自动猜测。IP之外的字段都有安全默认，无需修改驱动源码。
5. 保存完整候选配置，经原CAS/受控重启激活。包含必需SDK的组合，即使Simulation也要有原签名 DeploymentQualification。`Prerequisites`必须绑定实际精确Package和manifest中 `vendor-sdk/MvCodeReaderCtrl.dll/2.0.0.4` 声明，`Files`包含整个SDK目录全部DLL的绝对路径/hash（最多64）。保留manifest中 `Preload=[MVGigEVision.dll,MvSmtDevCtrl.dll]` 的精确顺序并随声明签名；只初始化这两个传输依赖及主DLL，其余DLL仅作安装字节校验/锁定；原生依赖限定本目录和System32，无PATH/工作目录修改。文件及祖先必须符合既有受保护程序路径/ACL政策；本地任意可写目录不能冒充批准的SDK安装。使用原 `scripts/sign-deployment-qualification.py` 离线签署已批准文档，不生成虚构批准人或现场证据。

Production仍需真实reader、维护认证、全部必需能力及未过期精确组合资格；本轮未取得这些完整批准，保持NotReady。连接失效、码回调非法、计数器反向/回绕、队列满或宿主拒收会锁定Faulted，不盲重连/重发。恢复由原应用命令守卫检查实体及原请求；软件暂停不是安全急停。

## 验证事实与未验部分

本轮Windows本机真实SDK已枚举1台设备，按IP打开、注册读码/异常回调、开始采集，有限窗口健康确认并正常Stop/Close/Destroy，未改设备参数。早期单独加载主DLL曾返回0x8002000B（动态依赖未加载）；固定完整路径预载SDK动态传输依赖后解决，不修改系统配置。健康更新同时要求重新发现原IP/序列号和SDK参数读取，避免仅靠可能缓存的GenICam值续期。3秒是本驱动的一秒检查存活租约，不能作为厂商安全时效或实体许可。

定向自测验证ABI前缀/长度/编码、无码/多码、同触发多帧/同码新触发、旧epoch回调隔离、计数器回绕拒绝、物理重复占用、背压及阻塞消费时保有、原生Stop失败重试、健康失败；签名包静态接受、无声明/错库/重复声明拒绝、ALC实际启动缺SDK资格拒绝、过期/错包/缺原生文件拒绝。具体冻结源码和结果见交付证据。

**真实读码帧：尚未收到；本轮连接窗口计数为0。** 实际扫码内容/码制/触发、物理拔网重连、真实双相机、长期SDK回调/内存/节拍、Application真实资格装配、RCS端到端和现场验收仍待验证。本轮不修改历史D-01/05/06/14批准或RV状态，不宣称旧HTML/原迁移完成，不变更仓库公开范围。

本轮曾在独立只读诊断进程尝试逐一初始化SDK目录全部DLL，初始化阻塞后终止该诊断。由此收紧为签名Preload清单；未将该尝试计为成功验证，也未修改或安装任何SDK文件。

发布冒烟发现并修复原宿主自包含模式的框架身份识别缺陷：不再把CoreLib同目录全部DLL当框架，改从冻结宿主deps清单的明确Microsoft.NETCore.App runtimepack逐项核验身份；共享运行时仍只使用真正shared/Microsoft.NETCore.App目录。原自包含签名测试失败保留，不移除断言。最终驱动候选升为1.0.1；先前本地1.0.0工程包保持原SDK/宿主指纹，不覆盖。

## 冻结交付回执

最终实现及构建源码为 `ef553272327cd6c4f2a9509563d4e9609f18cdc4`，工程签名驱动包1.0.1、Sportmaster1.4.1；未发布Production介质。完整机器私有连接输出留在Git外。[验证汇总](evidence/hikrobot-reader-01/verification.json)、[实际自包含测试stdout](evidence/hikrobot-reader-01/self-test.log)、[签名包身份](evidence/hikrobot-reader-01/package-identity.json)和[原自包含失败](evidence/hikrobot-reader-01/self-contained-before-fix.json)分别保留范围。原生WPF仅构建通过，本轮没有重做UI交互验收。

最终26项驱动断言及5组签名/原生政策验证通过，最终自包含程序真实连接5秒、0帧/0码、停止释放成功。工具单测在bundled Python/Pillow下为9通过/1环境错误；原main归档单测6通过/1相同WinError1314，无符号链接权限。没有跳过或移除该断言，也未把整套工具单测记为PASS。早期CRLF导出导致内容身份差异的资料检查按原Git blob重新核验，不修改文件原始证据来获得PASS。

规范Git原字节完整资料核验：起始main及最终代码候选均267项既有错误（265项历史PPTX媒体类型、2项历史元数据标记），新增0；见[差异结果](evidence/hikrobot-reader-01/archive-delta.json)。后续证据说明提交仅增加本包回执/哈希，另核验其清单和链接，不反复扩大运行测试。
