# PARAM-UX-02 / PS-05 原生交付验证

本轮基于执行时最新 main `767075abe62a77c30e70766bb5d3f1feca02686b`，开工工作区干净、无开放 PR，继承 PR78/79。业务与打包修复提交 `c730a73a0f70f88d9815f3ad431ef90fab9553ce` 已推送至 [PR80](https://github.com/yuan2go/importer-pc/pull/80)。最终工程介质冻结为 GitHub 核验合并树 `bfec0a5672d846a865db62af9ef6a573a953f914`；源码、夹具、截图、安装器内嵌介质共用此身份。1.7.0 为新构建，旧 1.6.1 未重命名复用。后续文档提交不改变冻结介质身份。

## 实现及原生复核

- 无差异候选在 UI、保存审阅、Application Stage/Inspect 中均为 Unchanged，不生成 Pending、不推进配置 Revision。保留暂停、维护授权、原因、CAS、RuntimeId、冻结候选及 Unknown 原提交核对。
- Sensitive 与 secret-reference 使用 PasswordBox；导航和首配保留真实引用，审阅遮罩覆盖未带 Sensitive 标记的 secret-reference，UI Automation 不返回真实引用。
- 相机及单共享 RCS 从 Effective 和实际快照显示运行地址、提供者版本、配置标识和当前证据；Pending 独立，失效快照显示未知。RCS 本地就绪明确不等于远端接受。对象诊断复用已有路径，返回缓存表单；未新增 Probe 或业务试单。
- 保持四类单目录、集中表单和统一保存。相机连接、取格口/心跳编码、识别缓冲分别组织；高级 JSON 采用/读取后恢复表单，普通刷新不切走技术任务。业务正则、固定左右相机、独立首配与既有会话/启动回退合同继续复用。
- 修复错误原文校正离焦后仍残留旧错误、两种宿主在 Busy 时首开参数漏读、首配步骤返回时目录重选导致原输入对象不可见。仅等待既有任务结束后读取一次，不重建业务、不反复重试、不自行保存。
- 修复 CI 的 Windows 8.3 TEMP 路径映射：构建用规范路径，避免不同临时目录进入内嵌调试信息。真实双目录编译比对与安装器共享 DLL 字节断言均保留。

使用项目 UI/UX Pro Max 独立 `--stack wpf` 查询及 Impeccable Operate / WPF Assessment A，按 V3 高清图、原型源码、HANDOFF 与字段迁移表复核。对象标题、运行摘要、字段分组和统一动作层次清晰；原生焦点框、目录/内容独立滚动及紧凑底部动作保持可达。本环境拒绝 file:// 原型入口，静态读取不记作新原型交互 PASS；V3 原件 54 文件及哈希未改写。未使用网页/CDP作为 WPF 验收。

## 最终冻结验证

[Windows CI 39546557135](https://github.com/yuan2go/importer-pc/actions/runs/39546557135) 全部门禁通过。证据目录为 [final-bfec0a5672d8](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/DELIVERY.json)。

| 验证层次 | 结果与边界 |
|---|---|
| 冻结库存与构建 | Windows/portable 两目标库存、ZIP、源码与共享 DLL 一致；安装器重建 DLL 与内嵌 Windows 介质逐字节一致。 |
| 核心、恢复和原生页面 | core、wpf、removal、visual 均 PASS；首配返回原对象、经典布局忙碌后首次读取、审阅取消留稿都有原断言。 |
| V3 专项 | 142 项 PASS；同源发布 DLL 与同源夹具，FixtureMatchesMedium=true。涵盖无差异、Pending/Effective、敏感字段、输入/选区/草稿、错误定位、失效摘要和对象诊断。 |
| 尺寸/DPI | CI 实际 DpiScale=1；本机同源夹具实际 DpiScale=1.25，142项断言、41张 WPF 截图通过。客户区 1366×768、1092×614、800×420 DIP；本机125%最大化工作区域1536×816 DIP完成键鼠走查。渲染尺寸不等于独立目标 IPC 通过。 |
| 本机输入与任务 | 中文草稿、Ctrl+F/Enter搜索定位、Ctrl+A选区、无效原文、Tab离焦清错、目录留稿、按对象诊断往返、恢复原值后的保存禁用 PASS；中文文本插入不冒充完整IME组合测试。 |
| 发布启动 | 同源1.7.0安装器完整性准备/选项/改路径/取消；发布Desktop缺配置与已有合成配置的离线维护、恢复入口和正常退出 PASS。未执行完整安装、升级、重启或双账号验证。 |
| 真实接口/现场 | 全部设备、身份及配置均为显式合成夹具；真实 SDK/RCS、实体、权限、目标触控与现场连续运行 BLOCKED/NOT_RUN。Production 门禁按预期拒绝，不授予生产资格。 |

原生截图：[相机字段](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/parameters-right-driver.png)、[运行摘要](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/v3-rcs-running.png)、[敏感输入](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/v3-sensitive-reference.png)、[错误定位](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/parameters-save-validation.png)、[保存审阅](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/parameters-save-review.png)、[800×420](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/scd-evidence/parameters-v3/v3-size-800x420.png)。运行摘要中的 manual 能力确实没有可配参数；完整相机字段图来自公开 schema 的离线合成配置，不冒充真实 SDK。

本机最终证据：[原生夹具与DLL同源回执](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-native/RESULT.json)、[键鼠步骤](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-manual/INPUT.json)、[实际无效输入](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-manual/invalid-raw-input.png)、[离焦清错](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-manual/corrected-on-tab.png)、[启动回执](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-startup/RESULT.json)。安装器和缺/有配置启动均为实际发布EXE；业务交互使用该介质原DLL的合成运行环境。

## 交付及运行

[安装器与 Windows ZIP 下载](https://github.com/yuan2go/importer-pc/actions/runs/39546557135/artifacts/12321609497)；CI artifact 保留30天。本机最终介质位于 `.artifacts/parameter-v3-final-ci/`，EXE与ZIP下载字节均与CI回执匹配；ZIP的735个文件及源码身份通过既有 inspect_medium 校验。发布身份及完整哈希见 DELIVERY.json、INSTALLER.json、RELEASE.json 和 SHA256SUMS。源码仓库只保留获授权通用代码、合成证据及回执，不提交 EXE/ZIP、客户资产或 SDK。

| 产物 | SHA-256 |
|---|---|
| CainiaoSupplyStation_SM_Setup_v1.7.0_win-x64.exe | `0e12b65661a8fa34b528fea2d9ed93c865ba55355e71a81e5e89d395d84490e8` |
| sportmaster-win-x64-bfec0a5672d8.zip | `e3871d4dbd023ac5035555c34cb5561a5ab8acad6472733a8991f358e05f2a51` |

操作见[原生运行说明](../parameter-settings-v3-native-01.md)。正常安装后的无配置启动走独立五步首配；未登记解压介质默认进入安装准备。`desktop/Ipc.Desktop.exe --maintenance --config <隔离配置目录> --data-root <隔离数据目录>` 仅作离线修复，不建立部署信任、不启动业务设备。安装器未签名；完整安装/升级/重启/双账号及可信分发资格仍待授权环境验证。

## 中间证据与未验项

- 根目录 [ce9dcd3 DELIVERY](../evidence/parameter-settings-v3-native-01/DELIVERY.json) 及 native/manual/startup 是中间证据：142项原生断言、Windows125%、中文输入/Ctrl+F/Enter/Ctrl+A/Tab、错误修正/留稿/对象诊断，以及 EXE 解包/完整性/选项/取消和隔离离线启动。均保留原 SourceCommit，不扩张到本次冻结源码；中文文本输入不等于完整 IME 组合测试。操作反馈保留上次任务结果，本地错误清除以当前字段和错误摘要为准。
- [local-bf7edd4](../evidence/parameter-settings-v3-native-01/local-bf7edd4/README.md) 保留两个共享桌面消费者0警告/0错误构建与开发期确定性回归；它不替代最终冻结验证。定向保存/恢复39项、版本兼容17项及发布指南2项的原记录保留各自执行范围。
- [CI第一次](../evidence/parameter-settings-v3-native-01/ci-first-failed/RESULT.json)发现首配返回目录错位；[第二次](../evidence/parameter-settings-v3-native-01/ci-second-failed/RESULT.json)发现经典布局忙碌漏读；[第三次](../evidence/parameter-settings-v3-native-01/ci-third-packaging-failed/README.md)在全部原生门禁通过后发现打包路径问题。原失败与 f4ce7b6/9c801a1 历史证据保留，无删断言、吞异常或放宽成功条件。
- 磁盘空间不足期间先完成不依赖C盘的CI、下载和核验；随后实测C盘恢复约1.5GiB后才完成本轮最终介质启动及125%原生检查。自动审批先前拒绝删除本任务临时目录，仅返回 blocked by policy；未绕过，原证据保留。
- 本轮安装器在任务附加 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 且TEMP/TMP使用正斜杠路径时，两次在窗口出现前以 `0xC00000FD` 退出；[原事件](../evidence/parameter-settings-v3-native-01/final-bfec0a5672d8/local-startup/custom-extraction-failed-events.json)保留。恢复正常.NET提取位置及Windows TEMP路径后，同一哈希EXE通过选项/取消冒烟。两个环境因素同时改变，未证明精确根因，不宣称自定义提取环境已支持；正式启动使用普通Windows环境。
- 归档检查按 Git 对象字节与固定 main 比较，仍为相同298项历史问题，新增0，不改校验器或豁免。原 REQ/BR/CAPI/D/RV、原哈希、旧1.6.1及历史验收身份保留。
