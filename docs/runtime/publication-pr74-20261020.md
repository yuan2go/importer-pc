# PR74 合并与 SM 1.6.0 Windows 安装包发布

[下载安装包](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.6.0-1878be50d5be/CainiaoSupplyStation_SM_Setup_v1.6.0_win-x64.exe) · [Release 与校验文件](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.6.0-1878be50d5be) · [源码](https://github.com/yuan2go/importer-pc/tree/1878be50d5befdbb56249e5d39a61deb023fe51e)

用户本轮授权合并 PR74、按仓库流程发布同源 Windows 安装包并更新记录，只执行合并与发布实际风险所需验证。[PR74](https://github.com/yuan2go/importer-pc/pull/74) 已通过普通 merge 合入 main，实际合并提交为 `1878be50d5befdbb56249e5d39a61deb023fe51e`。合并前 main 为 `de2b045a6eb66c66cc22a237fb2c4e0f84f98e78`，PR head 为 `15241b2a200904ee1c55202d28348fc5b7df2782`；没有其他开放 PR，也没有覆盖原工作区修改。

## 安装与身份

| 项目 | 本次发布 |
|---|---|
| 安装文件 | `CainiaoSupplyStation_SM_Setup_v1.6.0_win-x64.exe` |
| 业务版本 / 文件版本 | Sportmaster 1.6.0 / 1.6.0.0 |
| 字节数 | 177640175 |
| SHA256 | `38b531ccb7b3c2f05e3a7a0c67395633a6bf66f80b070493590f73fd8391fbc4` |
| SourceCommit | `1878be50d5befdbb56249e5d39a61deb023fe51e` |
| 可核验构建引用 | `origin/main`，构建时指向上述完整提交 |
| GitHub tag | `installer-sm-v1.6.0-1878be50d5be`，固定指向该合并提交 |
| 签名与发布资格 | 未签名；GitHub prerelease；Simulation reference，未取得生产/现场资格 |

基础安装器自包含 .NET/WPF 与 SQLite，按[单窗口安装流程](installer-single-window-01.md)选择程序和数据目录；安装、首次工位配置与真实业务准入仍是不同阶段。[依赖与海康准备](installer-release-info-01.md)及[Windows 兼容范围](windows-client-compatibility-01.md)继续适用。海康 SDK、生产凭据/数据、测试信任和精确签名扩展闭包不随本 EXE 分发；Production 缺必需能力继续拒绝，不能使用图中示例替代现场配置。

根目录、app、desktop 的 BUILD-INFO 同源，ProductVersion 为 `1.6.0+1878be50d5befdbb56249e5d39a61deb023fe51e`。[交付身份](evidence/publication-pr74-20261020/DELIVERY.json)、[BUILD-INFO](evidence/publication-pr74-20261020/BUILD-INFO.json)、[发布范围](evidence/publication-pr74-20261020/PUBLICATION.json)、[SHA256SUMS](evidence/publication-pr74-20261020/SHA256SUMS)同步提供。EXE 内嵌 ZIP 为 106388013 字节，SHA256 `f773eada17f7c92e693ae35478c94d2bd23720e90cf34f81904801b30ff6d726`；733 个文件、解压内容 242330073 字节。内嵌 ZIP 未另上传，EXE 是用户安装入口。

## 本轮必要验证

| 验证 | 结果与边界 |
|---|---|
| 合并与 CI | 实际合并树和 PR head 无文件差异；[既有 CI 39440449649](https://github.com/yuan2go/importer-pc/actions/runs/39440449649)成功。其冻结候选为 `c2abe29508d4fc045f39a74b320bd9d05a66af0b`，不改标成实际合并 SHA；本轮未重复业务/UI套件。 |
| 冻结构建 | 用固定 Git 导出、SDK 10.0.402、locked restore 构建 app、真实 WPF 与单 EXE。完整清单/ZIP/每文件哈希、来源/业务/依赖身份通过，实际 CLI business-identity 读回 SM 1.6.0。 |
| 字节闭包 | app 与 desktop 12 个共用 Ipc DLL、安装器与 desktop 15 个共用 Ipc DLL 逐字节一致；封装输出恰好一个 EXE。 |
| 原生安装器冒烟 | Windows 11 Home 10.0.22631 x64、实际桌面125%。最终 EXE 准备→安装选项为同一观察到的 HWND，正确显示1.6.0；取消正常退出，自动清理本次准备目录，原部署登记哈希不变。没有点击安装，没有 UAC、完整升级或回滚重验；没有高频窗口闪动采样。 |
| 原生客户端冒烟 | 同一介质 desktop EXE 指定隔离缺配目录，进入“启动修复·离线维护”；关闭后进入恢复页，再“停止并退出”。没有业务数据库文件、没有设备或业务启动。 |
| 上传与下载 | [上传回执](evidence/publication-pr74-20261020/artifact-upload-receipts.json)核对远端资产大小及SHA256；[公开下载核验](evidence/publication-pr74-20261020/public-download.json)从无认证下载链接回读最终EXE，完整字节数/哈希一致。 |

[介质核验明细](evidence/publication-pr74-20261020/medium-verification.json)、[介质构建日志](evidence/publication-pr74-20261020/media-build.log)、[安装器构建日志](evidence/publication-pr74-20261020/installer-build.log)、[原生图片来源与哈希](evidence/publication-pr74-20261020/native-evidence.json)、[退出后核对](evidence/publication-pr74-20261020/post-exit.json)保留原结果。新安装器的[准备画面](evidence/publication-pr74-20261020/installer-preparing.jpg)和[选项画面](evidence/publication-pr74-20261020/installer-options.jpg)来自实际 EXE；[离线维护](evidence/publication-pr74-20261020/desktop-maintenance.jpg)和[恢复页](evidence/publication-pr74-20261020/desktop-recovery.jpg)来自实际同源客户端。

最初两次发行前置检查因 Windows 检出历史 `aux.js` 保留名称造成索引异常而拒绝，尚未构建；[原拒绝日志](evidence/publication-pr74-20261020/preflight-rejection.log)保留。仅修复独立工作树索引与本地 sparse 排除，保留两版 aux.js 的 Git blob，恢复原日志的准确字节。之后工作树干净再冻结构建，没有修改源码或降低打包检查。用户原工作区保持原分支和内容。

## 已知问题与待验

[审查意见](https://github.com/yuan2go/importer-pc/pull/74#discussion_r4326168214)指出：schema-3 日常配置的 `CapabilityDraft` 没有首配中的 `operator-login`/Bound 投影，因此已绑定 Login 的 `SessionLifetimeMs` 未显示在日常四分类表单。本次按源码确认问题，未在合并发布范围修复或重测；不声称全部 review 已关闭。现有配置值仍由原策略/配置账持有，不能把隐藏字段解释为无需会话策略。

新 EXE **没有**重验完整安装/UAC/升级/回滚、目标工控机、Win10 Home/Pro、其他DPI/触摸/CPU、干净受支持系统、精确签名扩展闭包、真实SDK/相机/RCS及现场物理流程。历史1.4.1的58项安装结果、原7c的41项结果保留各自来源；不会转记成本版结果。此前[32张WPF图集](../ui/sportmaster-v2-wpf-20261020/README.md)与[UI验证](evidence/ui-task-redesign-v2/ACCEPTANCE.md)仍以26bb生产构建和各夹具来源为准；本次生产代码相同，但重新构建的程序集包含1878来源信息，不称旧图为本次DLL重新截图。

本发布记录及上传回执在源码冻结后补充，经独立文档 PR 入 main；EXE 与其随包指南始终保持1878原字节，不改包、不改 tag、不覆盖旧资产。此次公开范围是获授权通用代码、合成/隔离证据和新安装产物，不改变可见性，不包含客户原稿、SDK、字体包、密钥或生产数据。

## 复现

在该完整提交的干净工作树运行，`origin/main` 须确实指向该提交；后续复现可用等价的已核验本地引用，不伪称当时不存在的分支：

```powershell
python scripts/package-application.py --application sportmaster --target win-x64 --source-branch origin/main --output .artifacts/pr74-release/sportmaster-win-x64-1878be50d5be --dotnet D:/tools/dotnet/dotnet.exe
python scripts/package-installer.py --media .artifacts/pr74-release/sportmaster-win-x64-1878be50d5be.zip --output .artifacts/pr74-release/installer --dotnet D:/tools/dotnet/dotnet.exe
```

输出目录必须全新；按实际SDK位置调整路径。PackagedUtc 等构建元数据反映每次实际执行时间，不承诺重新打包的整个EXE字节等同此处已发布原件。
