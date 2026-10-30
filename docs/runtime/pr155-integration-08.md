# PR155、PR156、PR158 整合记录

2026-10-30，工作包 [PR155-INTEGRATION-08](../work-packages/WP-IPC-PR155-INTEGRATION-08.md)。用户授权先合 PR156、PR158 到 PR155，再合入 main；不包含真实安装或设备操作。

## 其他任务完成情况与代码审查

| 来源 | 已完成 | 仍未完成 |
|---|---|---|
| [PR156](https://github.com/yuan2go/importer-pc/pull/156)，`219fd5dfff4a5c23578abe52f407e08cc6fec05e` | D 盘本地卷/ACL/映射/EFS 预检、SYSTEM 拒绝访问检查、受保护新程序目录建立、服务原 API/Win32 错误与恢复失败保全、固定回执和阶段日志；已有针对性证据 | C 程序/D 数据、全 D 两组真实 SCM 矩阵均 NOT_RUN；目标机 1783 具体 API 和根因未确认。注入 1783 只验证错误保全 |
| [PR158](https://github.com/yuan2go/importer-pc/pull/158)，`0ce83993ede9d96735690126b7ffc70430e1985c` | 从首次修改前的准确父提交补回 INDEX 与 MVS 指南原始 Blob，附来源/大小/哈希及禁用文本转换规则；无生产代码修改 | 不替代原相机性能或现场验收 |
| [PR155](https://github.com/yuan2go/importer-pc/pull/155)，原头 `9eb90018f5683b3b7e63abf7b00b275169ec6c61` | 单轮调试、同帧图/实际定位、冻结请求、显式发送、再次扫码、Unknown/轮次/资源所有权、首页输入与图片查看；已含 main 的 PR157 | 真实双相机/PDA/客户 RCS、SDK 缓存和在传输帧边界、拔插重连、高速长稳、现场物理责任和完整发布安装验收仍待完成；普通生产手工补录到内置 RCS 的来源合同不由隔离调试证明 |

审查 PR156 的服务登记、追踪、恢复、存储路径及预检实际代码：仅 OpenService 1060 可进入新建分支；原 API 错误在诊断失败和恢复失败后仍保留。新原生句柄先交还调用者所有权，再结算诊断失败；诊断写入故障不阻止恢复原服务。程序目录沿受保护祖先新建，原目录 ACL 不自动放宽；SYSTEM 静态检查不冒充真实服务进程写入。PR156/158 没有未解决审查线程；PR155 的可空筛选问题已经解决，原 22 场景/113 断言保留原身份。

## 实际整合

main 基线 `9df3901ec20d73512d48920b4671b529ebc58a9e`。本地顺序合并 PR156 为 `b2e23080`，再合 PR158 为 **`fba708de80438dd5d264b01d111395989de50bff`**；该完整提交是下列软件验证的 SourceCommit。保留来源父提交，不 squash、不 force push。

两次实际冲突均只有 `.gitattributes`、`docs/manifest.json` 和 `docs/manifest.sha256`。属性规则合并双方精确字节要求；manifest 从完整暂存树重新生成，并记录三个来源 manifest 的身份。INDEX 和验证程序入口自动合并后另行核对，双方生产代码未冲突、未重写。

独立核验确认 PR155/156 的 **61 份生产代码文件**与各自原头 Blob 完全一致；两份 PR158 补档在原提交、整合 Git Blob、工作区和独立 `core.autocrlf=true` 检出中逐字节一致，父提交关系、长度、Blob、SHA256 全部匹配。当前 MVS 指南与性能报告没有被原档覆盖。07 轮 **64 份**原始日志/合成截图的长度和哈希未变。证据见 [08 目录](evidence/pr155-integration-08/README.md)。

## 本轮必要验证

| 检查 | 本轮结果 |
|---|---|
| Application `--camera-rounds` | 36 条断言通过；真实 MVS/ZXing/SQLite/内置 RCS 代码，合成原生接口和本机 HTTP |
| 原生 WPF `--camera-round-ui` | 19 条断言通过；真实 WPF，合成设备和本机 HTTP，不外呼客户接口 |
| `--installer-storage-checks` | 5 项通过；真实隔离目录 ACL 负向预检、无写入预检、错误/恢复/脱敏责任 |
| `--installer-options` | 29 项通过；隔离目录及快捷方式 COM，不代表机级安装 |
| `--installer-change-guards` | 8 项通过，另有汇总行；合成身份/权限，不执行 Windows 恢复或真实设备 |
| `scripts/test_release_documents.py` | 3 项通过 |
| Application 验证程序、WPF 验证程序、Sportmaster Desktop 构建 | Release、锁定还原、各 0 警告/0 错误 |
| 原始字节、生产 Blob、历史证据 | 全部通过；脚本与机器可读结果随证据保存 |

运行前执行 `. ./scripts/project-environment.ps1`，构建使用 `-c Release -p:RestoreLockedMode=true -m:1 -nr:false`；Application/WPF DLL 分别接受工作区绝对路径和上表参数。只重跑本次受影响检查；PR157 原图/普通 MVS/探针此前 26/15/73 条断言仍属于 07 原 SourceCommit，不伪称本轮重跑。旧备份三项基线失败未关闭，历史 D/RV 不改写。

GitHub main 分支公开元数据在整合前显示 `protected=false`、必需检查列表为空，rulesets 返回空列表；保护管理详情端点返回 403。本次不修改任何规则，仍由标准 PR 合并接口执行服务端检查，不使用管理员绕过。GitHub CI 保持停用。最终 PR 状态与 main 合并提交以实际 GitHub 合并结果为准。

本轮不启动真实 SCM/安装、不更改现有部署或 C 盘业务数据、不连接设备或客户 RCS。源码合并与本机软件验证不能转记为可投产或现场问题已解决。

首次 WPF 调用的 PowerShell 宿主退出 `-532462766` 并报告 `Out of memory`，重定向日志为空、没有终局 WINDOWS-WPF.log，不能计为通过；原失败文本和空日志保留。随后用独立进程及文件重定向运行相同 DLL/源码/断言，退出 0，实际 WINDOWS-WPF.log 包含 19 条通过断言和三张合成原生截图。未改变产品或断言来取得通过；第一次内存不足的具体成因未定位，本次通过不作为长稳证明。所有当前索引改动前的 main、PR155 原头与合并后版本均保留[原始字节和来源清单](../archive/pre-pr155-integration-08/manifest.json)。
