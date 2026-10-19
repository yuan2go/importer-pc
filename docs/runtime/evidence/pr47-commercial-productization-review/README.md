# PR #47 复审交付证据

当前冻结运行源码及夹具：`022c898f138a1326eec9134f8f18724a07fea5c4`；业务1.4.0 / Application.Contracts1.10.0 / DB8。复审起点为`c74c01e`；旧`bd86fbb`介质及[原记录](../commercial-productization-01/README.md)完整保留。本轮文档回执提交号及main合并号不作为运行介质源码号。

[审查裁决与COMM映射](../../../reviews/pr47-commercial-productization-review.md)在编码前形成；[逐项结果](checks.json)、[六介质身份](media-identities.json)记录最终结果。所有介质均在本机生成，未自动上传ZIP或任何私有备份/密钥。清单中的相对本地路径在仓库根解析。

## 受影响验证

- [核心回执](core-result.json)及[日志](core.log)：实际发布DLL上的首配、未确认失败/候选/中断/CAS、Windows DPAPI、原账/原库缺失保护、完整恢复集、计划备份、原机来源/旧来源拒绝、Unknown原请求不重发、历史/导出/取消。
- [原生WPF回执](wpf-result.json)及[日志](wpf.log)：实际发布的独立首配窗口，真实crypt32拒解合成坏密文时仍加载原配置字段、明确草稿不可用、保全原密文；两布局共享实例、历史长码/焦点、设置与支持页面。截图：[首配](commercial-first-configuration.png)、[历史](commercial-history-1100x600.png)、[支持](commercial-support.png)。测试没有执行安装、设备或提升权限。
- [补充原生可操作性](native-readiness.log)：Dispatcher空闲后，实际首配输入框及保存/启动按钮均可用，原拒解密文不变。[探针源码](FirstConfigurationReadiness.cs.txt)作为独立补充证据保留，散列记入checks；不改冻结运行源码或原夹具。
- [卸载内核回执](removal-result.json)及[日志](removal.log)：实际发布Platform DLL；整清单先校验、越界拒绝、外来文件保留、已删根重入、根被文件替换拒绝、真实Windows文件共享占用与释放后重入。重启队列回调是合成依赖，实际MoveFileEx/受保护卸载/重启仍NOT_RUN。
- [组件回执](component-result.json)及[日志](component.log)：独立新版本参考签名包1.4.3，只用于Simulation/TestOnly静态审阅，不安装/加载插件；旧1.4.2不改标。本轮包身份见[参考包构建](reference-extension.json)，私钥和信任文件仅保留在本机私有目录。
- [介质日志](inventory.log)：六ZIP及清单、成员字节/CRC、业务/源码/契约/存储、app/desktop公共DLL及启动入口。同一冻结Sportmaster Windows介质用于上列关键旅程。
- [旧读取者](old-reader.log)：实际原冻结70ccedc DB7 DLL拒绝本轮DB8，拒绝前后DB散列相同。
- [本机非提权预检](permission.log)：受保护程序父目录预检不创建目录；可写祖先拒绝；无提升执行返回5，数据和唯一登记无变化。

## 复现

在仓库根使用本机SDK10.0.402或匹配的受控环境。构建前检出上述精确源码；需要新输出目录，禁止覆盖已有介质/回执：

```powershell
python scripts/package-application.py --application sportmaster --target win-x64 --output .artifacts/comm-review-47/releases/sportmaster-022c898-win-x64 --dotnet D:/tools/dotnet/dotnet.exe
pwsh -File scripts/verify-commercial-media.ps1 -Media .artifacts/comm-review-47/releases/sportmaster-022c898-win-x64 -Output .artifacts/comm-review-47/frozen-core -Dotnet D:/tools/dotnet/dotnet.exe
pwsh -File scripts/verify-commercial-wpf.ps1 -Media .artifacts/comm-review-47/releases/sportmaster-022c898-win-x64 -Output .artifacts/comm-review-47/frozen-wpf -Dotnet D:/tools/dotnet/dotnet.exe
pwsh -File scripts/verify-commercial-removal.ps1 -Media .artifacts/comm-review-47/releases/sportmaster-022c898-win-x64 -Output .artifacts/comm-review-47/frozen-removal -Dotnet D:/tools/dotnet/dotnet.exe
```

对DirectPlc/WcsToPlc及portable按同一源码分别出包，不将portable当原生UI验收。客户运行：完整Windows介质`Setup.cmd`→指定账户非提权运行`desktop/Ipc.Desktop.exe`→首次配置/受控启动；失败用`Maintenance.cmd`。完整规则见[随包操作指南](../../commercial-productization-01-operations.md)。不修改当前受保护机器政策，不连接真实设备来复现合成夹具。

## 保留的真实边界

Windows提权安装/UAC、第二账户ACL与跨用户DPAPI、系统登记切换/修复/卸载、占用文件实际重启删除、真实第二机器、1440×900有效客户区/触控/完整DPI和读屏矩阵：**NOT_RUN**。真实SDK/客户协议/设备/组合资格、换机来源退役与秘密/外部编号交接、现场投产：**BLOCKED / NOT_READY**。旧库缺机器来源证据的自动原机接管：**BLOCKED**，仍可完整备份和隔离恢复。D/RV及历史批准/验收未改写。

`bb90598`、`a142817`两批中间候选在追加审查/夹具核对后中止，未作为发布或PASS；原日志及中止原因留在本机`.artifacts/comm-review-47/`。首次开发构建因验证项目漏引既有组件审阅文件失败，补齐引用后的构建及实际冻结DLL结果分别保留，不用后者改写前者。
