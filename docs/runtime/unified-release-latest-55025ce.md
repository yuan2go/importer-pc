# 完整同源 Windows 发行 · 55025ce

状态：八个实际 ZIP 已核验并[发布](https://github.com/yuan2go/importer-pc/releases/tag/unified-main-reference-55025ce)，共11项资产（八包、RELEASE.json、build-record.json、SHA256SUMS）；公开 API 回读大小及 digest 全部匹配。本地发行目录 `D:/r/latest-55025-final`，实际记录见[证据](evidence/unified-latest-55025ce/README.md)。这是参考预发行，未取得商用/现场合格结论。

运行源码 **55025ce9c62173fea5cdc5ceecfb29453ed5e7fa**，source tree `789527f8a43d54c9c77b9a3cf5731172f295d36f`，包含 PR44/45/46 与 PR53 的缺失原数据库拒绝新建空库修复。原构建 main 基线 `f3059d499cd4dbc60b443f0a54cac939b2e5db2f`；后继 `f3059d499cd4dbc60b443f0a54cac939b2e5db2f` 在源码/配置/打包脚本/SDK/产品指南范围无增量。55025ce 与 main 只有既有测试修正及打包器注释差异，Python AST 相同另记，不能声称全部仓库字节相同。业务1.4.0 / Application.Contracts1.10.0 / DB8；Supply/Inspection 仍为各自1.0.0 portable，没有同等原生安装器。

构建控制器与运行源码分开：初次补构建控制器见原 FAIL 回执，当前成功组合控制器 `c32f33bf8d6d8ead3e68d5a1d185e8040f371cc2`；随后只修正其 Windows 深路径清理并通过14门禁及实际残留目录清理。原六包从 PR53 按原文件名和 SHA 继承，Supply/Inspection 从隔离精确 Git checkout 实际构建。八单包完成后首次集合因临时 Git 目录清理失败，未产生成功 RELEASE；失败原件保留，再按独立身份清单逐一验证到全新目录，成功回执不覆盖失败。本次没有把 cfe6c0e 或 939462b 的媒体改标。

## 发行链路与必要核验

沿单一 `scripts/release-windows.py`、既有 package-application/package-business、依赖锁和指南导出。指定 --source 必须是 main 祖先；C#/配置/指南及打包逻辑差异拒绝，仅测试/控制器和 AST 相同的 Python 文本差异分别留证。源码/树/SDK10.0.402/运行时10.0.13/Windows11 build22631/Python3.14.3/命令/原始清单/日志/每包依赖随回执留存。输出目录已有即拒绝，八目标全部成功才写整套成功回执。

|链路|实际范围与结果|
|---|---|
|源码、版本、合同、依赖|八包 SourceCommit、业务/合同与 deps/兼容声明一致；App/desktop 共用 DLL 字节一致；遗留业务1.0.0单独验证|
|完整性与安装入口|八包库存、全部 ZIP 成员/内容/CRC/SHA、重复项及路径核对通过；三业务 Setup.cmd/Maintenance.cmd 正确派发；六个 app/desktop exe 是 PE x64|
|启动与身份|本次八个 Windows 实际只读/遗留入口缺机器策略均拒绝，配置/data 未创建；完整首启安装不在该检查范围|
|配置修复及原请求|按同一库存和 DLL 继承 PR53 核心证据，覆盖拒绝修复保留原字节、原账/Unknown/CAS/备份恢复和原数据库丢失拒绝；不重跑或改写历史结果|
|桌面、维护及扩展|继承同源原生有界 WPF、卸载内核及 TestOnly1.4.4静态签名/依赖/冲突/篡改/Production拒绝证据；未装载或执行提供程序|
|构建机制|14项定向门禁通过；实际只读 Git pack/长路径清理通过；入口后八包复核通过；11资产公开 API digest/大小一致|

跨平台可运行的打包/依赖/静态验证与当前 Windows 主机的有界运行结果分别记账。完整提权安装/UAC/ACL、第二账户、受保护升级修复卸载、安装中断/升级恢复、重启删除及完整目标桌面交互仍 **NOT_RUN**；真实 SDK/设备/协议/现场 **BLOCKED**，Production **NOT_READY**。无证书签名；没有以一次成功声称八 ZIP 再构建逐字节可重现，历史 D/RV 保留。

## 下载、安装与复现

完整解压所选业务 win-x64 ZIP，运行 `Setup.cmd`，按向导指定运行账户。安装后指定未提权账户从开始菜单启动；独立维护入口 `Maintenance.cmd`。portable 是对照或遗留参考装配，不能充当相同 Windows 原生安装器。可选扩展遵循当前签名/信任/版本隔离机制，不继承旧包的生产资格；当前无真实生产扩展批准。

```powershell
# 当前控制器，从独立原始清单继承本次八包；目标必须全新
python scripts/release-windows.py --source 55025ce9c62173fea5cdc5ceecfb29453ed5e7fa --reuse-directory <完整解压并保留原名的八包目录> --reuse-manifest docs/runtime/evidence/unified-latest-55025ce/original-eight-inventory.json --dotnet <SDK10.0.402路径> --output <全新目录>
python scripts/release-windows.py --verify-only --output <已完成目录>
# 从零编译：检出精确 55025ce、干净源码、匹配 SDK，再运行其原单一编排入口（无复用参数）
python scripts/release-windows.py --dotnet <SDK10.0.402路径> --output <另一个全新目录>
```

已有手动 Windows Actions 工作流保留，补 `fetch-depth: 0` 使固定祖先来源可核验；本轮未执行 Actions。较早 cfe6c0e、原 e79a8bf/16ecb57、7acbb2c、其他冻结候选以及独立的939462b不测试发行均保留本来身份与边界。

|ZIP|字节数|SHA256|
|---|---:|---|
| sportmaster-55025ce-portable.zip | 18501079 | `1DFB654161746A7235DD78D134B9F52D8B63414F81F422BE84EEF4C658D322CE` |
| sportmaster-55025ce-win-x64.zip | 104281721 | `6B84F8ABDF534FBA418C5D043F2E4A949EE6177C40A4421AFA2A7660AC27D051` |
| direct-plc-55025ce-portable.zip | 18463903 | `9E3F0107D0EF759D57CB0C5BB1FEAA72620F75E94055BDF4545605F4A96A9F84` |
| direct-plc-55025ce-win-x64.zip | 104197137 | `93867243C91A7AF6B8F30D66F6111FAAE9FD302C9822BD8BBB3F7005A36DA697` |
| wcs-to-plc-55025ce-portable.zip | 18485650 | `A53F21A9F55D1B19C0FA4B4403A3DA2D3CD03490992E8DA703673B7824075345` |
| wcs-to-plc-55025ce-win-x64.zip | 104240487 | `0F3DF4D6697F906A163785061F5C0459BDF67273984EAF31D0AAFF5CF1DF33F5` |
| supply-portable-55025ce9c621.zip | 18513321 | `4575669FA312588179ECD08081883E1CBF81C6D31DD4DB84E8114BA05748FF51` |
| inspection-portable-55025ce9c621.zip | 991033 | `C71FA4F2E1501A8DE043E8ECEECD2971B9CF366CEA9EDB603AC5D68F6AE17320` |
