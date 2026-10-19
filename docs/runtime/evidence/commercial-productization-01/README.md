# COMMERCIAL-PRODUCTIZATION-01 验证与介质回执

运行/打包冻结源码：`bd86fbb01b249f07e25fb12e003fed2a130a9c1c`；开始main：`c445b0ec32a3db44f00a1968f3c501aa36eb786f`。文档/开发验证辅助脚本的后续提交不替换运行身份。SDK10.0.402、Windows当前非提权账户。本站没有被安装机器政策、生产参数或真实设备资格。

- [文档/清单校验](document-check.log)与[复核脚本](verify-delivery-docs.py)：规范Git字节、未变记录/历史对象、原件、D/RV与原UI、当前新增链接/锚点和公开增量；仅原始证据保留Windows行尾/终末空行，未修改归档检查器或业务测试守卫。
- [逐项结果与剩余](checks.json)：COMM/CPV软件覆盖、原生边界、NOT_RUN/BLOCKED和命令。
- [六介质精确身份](media-identities.json)：三个Application的Windows与portable ZIP SHA256/大小、逐文件清单SHA256及文件数量。
- [冻结核心回执](core-result.json)及[原日志](core.log)：实际发布DLL、同提交夹具、介质前后不变。
- [冻结WPF回执](wpf-result.json)及[原日志](wpf.log)：原生1100×600任务页、焦点/长码/共享实例；[历史页](commercial-history-1100x600.png)、[帮助支持页](commercial-support.png)。合成身份/端口不代表真实设备。
- [冻结静态组件回执](component-result.json)、[原日志](component.log)和[独立新包身份](synthetic-package-identity.json)：只接受Simulation/TestOnly参考barcode包；原包不变，无代码加载/安装/激活。篡改、缺精确依赖、冲突版本和Production信任均拒绝。
- [原生只读权限](native-permission.log)：保护父目录预检不创建目录；可写祖先拒绝；非提权批量执行返回5且不改业务数据/登记，无隐藏UAC。
- [真实旧冻结读取者](old-reader.log)：70ccedc DB7 DLL拒绝最终DB8，数据库SHA256不变。[SQL1～7原文](sql-floor-identity.log)未改。
- [六包完整检查](media-smoke.log)：清单、ZIP完整成员/CRC/内容、业务身份、所有公共Ipc DLL的app/desktop字节一致；`package-*-bd86fbb.log`为六份原始出包记录。

## 复核方式

完整介质保存在仓库本地 `.artifacts/comm/releases/<application>-bd86fbb-<win-x64|portable>[.zip]`。其中 Windows 双击 `Setup.cmd`，随后从指定账户的开始菜单非提权运行；独立启动修复/维护用 `Maintenance.cmd`。先比较本回执的ZIP与包内SHA256SUMS，不从其他版本取DLL拼装。Production仍须取得真实合同/实现/设备/账户/秘密/范围资格。

在本仓库使用固定SDK；以下命令均选择新的输出目录，引用冻结介质，不提权、不安装机器政策、不启动真实设备：

```powershell
$media = '.artifacts/comm/releases/sportmaster-bd86fbb-win-x64'
$dotnet = 'D:/tools/dotnet/dotnet.exe'
./scripts/verify-commercial-media.ps1 -Media $media -Output .artifacts/review-core -Dotnet $dotnet
./scripts/verify-commercial-wpf.ps1 -Media $media -Output .artifacts/review-wpf -Dotnet $dotnet
./scripts/verify-commercial-components.ps1 -Media $media -Package <独立新版本TestOnly参考barcode包> -Trust <独立TestOnly信任文件> -Output .artifacts/review-components -Dotnet $dotnet
python docs/runtime/evidence/commercial-productization-01/verify-inventories.py --releases .artifacts/comm/releases --source bd86fbb01b249f07e25fb12e003fed2a130a9c1c --output .artifacts/review-inventories --dotnet $dotnet
```

独立扩展用 `scripts/build-extension.py --kind observation --version <新的不可变版本> --contract <冻结app/Ipc.Platform.dll> --check-host <冻结app/Ipc.Assembly.dll> --output <新包> --key <本地合成私钥> --publisher <合成发布者> --trust <仅TestOnly信任> --dotnet <固定SDK>`。私钥和实际信任不进入仓库或交付介质；本次1.4.2静态包身份只证明对应构建，不给旧包改标，不代替运行组合声明或Production资格。重建源码时从冻结commit干净检出，运行原 `scripts/package-application.py`；新的ZIP时间/压缩字节不冒用本次ZIP哈希。

## 原失败、修正与未运行

保留原始日志，不把后来的PASS覆盖先前失败：

|记录|解释/后续|
|---|---|
|`baseline-build.log`、`baseline-startup.log`|实际基线；未提权机器维护权限门槛不改为假成功|
|`component-review.log`|7ca合法签名包静态解包根缺失；9692f4d修复，`prior-969-component-review.log`通过；最终另冻|
|`prior-969-media-smoke.log`|发现app/desktop公共DLL分叉；实际PDB指向9692f4d/dcfb8b1；最终显式SourceLink+出包前字节门禁|
|`prior-7ca-*.json/log`、`prior-7ca-media-identities.json`、`prior-969-core.json`、`prior-969-wpf.json`|早期介质局部检查，不冒称最终组合介质；旧ZIP本地保留|
|`synthetic-package-build.log`、`synthetic-package-build-r2.log`|Windows临时声明文件仍打开时删除失败；修正builder资源生命周期后新包成功；各版本保留身份|
|`extension-build-tests.log`|19项：18通过，符号链接夹具创建遇WinError1314；该条NOT_RUN，原断言保留|
|`original-archive.log`、`original-archive-02.log`|首次比较checkout CRLF散列失败；归档Git字节逐一核对通过，保留原checkout散列与澄清前库存|
|`build-supply-frozen.log`、`build-inspection-frozen.log`|早期受影响共享层编译0警告0错误；之后运行修改仅Parcel，不能改称本最终六介质上的Supply/Inspection旅程|
|`comm-complete-evidence.log`|开发收口检查，最终实际DLL核心回执另列；不把跨构建PASS混作同介质|
|`runtime-identity-02.log`|共享纯身份判定及首配回调回归，非真实跨账户/UAC旅程|
|`dependency-utf8.log`、`windows-frozen-archive.log`、`release-documents-frozen.log`|实际UTF8依赖许可、深路径git归档及固定指南验证，相关失败已有真实修复|

Windows特权安装/取消/切换中断、安装后ACL/系统登记/修复卸载/重启删除、两个真实账户、指定账户跨用户DPAPI、目标1440×900/高DPI/触控/完整读屏、长时真实负载逐项NOT_RUN。当前桌面工作区1536×816 DIP无法容纳原1440×900客户区断言；1100×600局部检查通过不关闭原矩阵。真实SDK/协议/设备/现场授权/生产签名与资格、换机秘密/退役/外部范围证明逐项BLOCKED。没有为此删除旧断言、改变D/RV、自动重发Unknown、重新创建空业务根或伪造现场资格。

完整维护备份包含原件与加密草稿，属于私有交接；本目录只公开通用源码、原日志、合成数据产生的有限结果和本项目自有原生页面截图，不含真实配置、数据库、客户原稿、秘密、私钥、生产资料、SDK/字体。
