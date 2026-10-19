# 2026-10-20 main 统一 Windows 发行

状态：统一参考媒介已构建、核验并实际发布；Windows 商用/现场验收没有通过声明。本任务在用户重新明确授权后继续必要构建与定向验证；此前中断、失败、旧 ZIP 和原发布对象保持形成时身份。

最终运行源码冻结为 main `cfe6c0e0d0570986ea8c63f619e328d3891b82ef`，包含 PR44/45/46/47/48/49/50 的合并成果。构建过程中后续 main aaa30b5 仅合并上传保管资料；核对 src/config/scripts/global.json/Directory.Build.props 与当前产品操作指南没有变化，不重标候选为该文档提交。业务版本 1.4.0、Application.Contracts 1.10.0、DB8；Supply/Inspection 仍是各自 1.0.0 portable 参考装配，没有同等原生安装向导。

沿唯一 `scripts/release-windows.py`：固定 SDK 10.0.402、完整 Git commit/tree、锁文件、工具链、命令和日志；既有打包器以 Git 导出编译，构建临时目录位于媒介外；SourceLink/PathMap 同源；app/desktop 公共 DLL、业务/ABI/兼容声明与清单对照；ZIP 成员、内容、CRC、文件库存、SHA256 和原生入口派发核验；八个目标全部成功后才产生 RELEASE.json。目录存在即拒绝覆盖，旧资产和中断输出保持。

本次只继承已合并的 C# 与发行机制，没有另建框架或修改 UI。原生安装/受保护 helper/最终登记、唯一配置账与 CAS、原请求责任及 Production 能力拒绝继续沿当前 main；源码存在不等于原生提权/现场验收通过。原 HTML/PNG 没有修改或重新验收。

## 实际媒介与来源

构建目录 `D:/r/main-cfe6`，整套身份和校验见交付证据中的 RELEASE.json / SHA256SUMS。8 个核心包及 1 个附加 Simulation 扩展组合均有实际字节；[公开发行](https://github.com/yuan2go/importer-pc/releases/tag/unified-main-reference-cfe6c0e)共 14 项资产，全部回读大小及 SHA256 与本地一致。构建及全部校验见[证据目录](evidence/unified-main-20261020/README.md)。完整解压 Windows ZIP 后双击 Setup.cmd；安装后以指定未提权账户从开始菜单运行；离线维护用 Maintenance.cmd。正常流程不要求 PowerShell；显式高级 .ps1 需要 PowerShell 7。

```powershell
# 在冻结 cfe6c0e 的干净 checkout 上重建，输出必须是新目录
python scripts/release-windows.py --dotnet D:/tools/dotnet/dotnet.exe --output D:/releases/<new-directory>
python scripts/release-windows.py --verify-only --output D:/releases/<completed-directory>
```

运行脚本不会安装机级政策、触发 UAC 或控制设备。安装者从可信渠道取得媒介；散列证明完整性，不等于发布者证书。未提供 Authenticode 证书，不宣称商业签名或工业认证。

## 风险定向验证

- 合并后发行门禁单测 9/9、随包指南 2/2 通过，保留 PR48/49 的原断言。
- 新入口原静态测试中三项通过，一项因 ParcelRuntime 改为默认绑定 MachineRuntimePolicy.RequireRuntimeIdentity 的内部 RuntimeIdentityPreflight 委托而定位失败；已修正为默认函数和委托调用早于配置/租约/存储的断言，装配入口原断言保留；4/4 通过，原失败留证。
- main 冻结 DLL：只读首配/内置目录/静态校验、无权限无数据创建、Pending/Effective 保真、CAS/owner 拒绝、Production 缺能力拒绝、无 DB/端口创建、种子字节不变通过。这里的授权委托是显式合成夹具，不是 Windows 提权验证。
- 扩展工具三项关键测试通过：关闭句柄后原子发布失败清理、冻结 checker 合同、Production TestOnly 拒绝。较早完整 19 项在修复后为 18 通过/1 WinError1314（本机无符号链接权限），未删断言或改为 skip；原日志保留。
- 新参考签名扩展 1.4.100：按当前宿主公共合同重新构建，独立 TestOnly 信任静态接受；篡改、Production 以及旧 1.3.100 宿主绑定拒绝。它不是批准的生产资格，未安装/激活，不执行模块或设备。

Windows 原生 UAC、程序/政策 ACL、第二真实账户、受保护安装、升级与中断恢复、系统登记修复卸载、实际桌面操作/DPI/读屏仍 NOT_RUN；普通账户无机级政策时的启动拒绝不代替上述验收。已有 DB 的受保护副本/提权维护、真实 SDK/协议/设备/现场、生产组合资格继续单列，不能用无 DB 的模拟修复关闭。D/RV、原历史失败与旧 HTML 评审不倒写。

本轮 Windows 主机上的 portable/core 检查与 Windows 媒介编译分别记账；没有重新运行 Linux/macOS 或真实现场，不把跨平台代码可编译当 Windows 实机验收。

## 出包身份

|Application|Target|业务版本|ZIP SHA256|
|---|---|---|---|
|sportmaster|portable|1.4.0|`D3D2912FFB80F7C9B82F1A02C66164D52EBD1E1BA1376F9416AFA5C2E28436D6`|
|sportmaster|win-x64|1.4.0|`EB883CF959391277C0B90FE96F88A5C1A3E926A1AD3153030FC923E024BD3043`|
|direct-plc|portable|1.4.0|`829BBF260C4CB28375C21D4782BCAA76AE56BFBF0F009E9B8C672764C634E0D6`|
|direct-plc|win-x64|1.4.0|`CE6C59617A6B0D4C54DBCDE5F85BEC79A7AAC9ACD172902476AD898C55DFCE11`|
|wcs-to-plc|portable|1.4.0|`B2BA753B67938C532C09EE7DC8030D4CFE01935B3C09B41A7FEE78FB93B49CA5`|
|wcs-to-plc|win-x64|1.4.0|`D9D7536196B213488A6D3233EA6C2526650A950F8234186D3CA81ED298ED8560`|
|supply|portable|1.0.0|`6DA39DA25AC5AB8CF97A91A8FE9B787901ED3A5A81A81F8A16604986977A424E`|
|inspection|portable|1.0.0|`B38187B415E2F0A264F644DF629169D57DC4E16A9A17FD6D95746BDCFC6AB064`|

附加 Sportmaster＋参考 RCS 1.4.100 的 Windows 媒体：`main-selected.zip`，SHA256 `689445552E78B5F6859BD0CF8BD7CA3B0724ADAE35F4A2590F4AD91CD04B6C25`。它与核心包采用相同冻结源码；两次独立发布的 app/desktop 719 个文件字节完全一致，扩展载荷及组合清单不同。安装/激活均 false；信任文件不在媒介中。没有重建全部八套来证明整包位级重现。

三套 Windows 媒体的六个 apphost PE Machine 均为 AMD64/0x8664；完整性检查实际接受全部三套及附加媒体，并在隔离副本拒绝额外 DLL、改写二进制及 Windows junction。八条明确只读/兼容运行入口和三个直接业务 Runtime API 均在缺失配置或数据访问之前拒绝机级政策缺失，测试结束仍无机级政策。

普通 Parcel 首次运行会执行 Prepare 并请求首次授权。初次负向探针误按“立即拒绝”设计，出现授权等待超时，已停止该尝试并保留原日志；第二次相同尝试亦中断，不记 PASS。随后改测显式 runtime-check 及真实业务 Runtime API；只在测试进程设置错误模式和禁用转储，不改变权限、政策、配置或设备。没有把首次授权等待当成 UAC 验收通过。

portable 在本次 Windows 构建中指 framework-dependent DLL 入口，可能附带 Windows apphost；它不是无依赖的跨平台安装器。本轮未在其他 OS 启动，不作跨平台运行合格声明。Windows 正常安装使用 win-x64 ZIP 的 Setup.cmd。

构建/测试/发布的原始日志、首次失败、源码冻结树、锁文件散列、依赖与许可证资料和远端回读全部已留证。源码及脚本的合并工作已在 main 完成；本次新增代码差异仅修复过期测试定位，不改变冻结运行 DLL。旧 e79a8bf、16ecb57、bd86fbb 和 07e0f7a/0f2cd71 的原始身份、失败与资格边界保持。
