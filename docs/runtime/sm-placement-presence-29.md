# SM托盘放置与取格口请求29

2026-11-02，[工作包](../work-packages/WP-IPC-SM-PLACEMENT-PRESENCE-29.md)。基线main `b16d3f93`。替代“每次独立识别交RCS去重”的旧合同；旧文档原字节见[归档](../archive/pre-sm-placement-presence-29/manifest.json)，历史RV与验收保持原SourceCommit。

## 运行规则

| 情形 | 结果 |
|---|---|
| 包裹放上，首次识别 | 建作业，发一次取格口请求 |
| 包裹留在托盘（等待RCS、取得格口后小车驶离前、RCS失败后） | 逐帧识别仍记观察，关联原作业；不建作业、不发请求 |
| 单帧或短暂漏读（短于1000毫秒） | 仍是同一次放置 |
| 取下包裹或托盘被带走，读码区连续无码≥1000毫秒 | 本次放置结束；之后再识别（含同码）为新一次放置，发新请求 |
| 换上另一个包裹（选中码不同） | 立即为新包裹 |
| RCS应答期间排队、观察早于空置开始的帧 | 处理时仍属原放置 |
| 键盘/PDA输入 | 不受限，按人工意图准入，并开始其放置 |
| 相机重连 | 放置保持不随epoch清除；需看到空置才结束 |
| 软件重启 | 各位以最近一件作业条码预置，看到空置后解除 |

现场RCS失败处理：包裹暂放一旁换下一件；或取下同一包裹再放上（让相机看到空托盘约1秒），即重新请求。

## 诊断

- 软件日志“扫码与准入/包裹已离开读码区”记录放置结束、关联作业和期间重复帧数。
- 每个重复帧的输入记录 `SamePlacementObserved`，原因 `SamePlacement`；调试级日志有“同一次放置的重复识别”。

## 验证

新增 `Ipc.Application.Verification --placement-presence-29`（合成MVS端口、真实ZXing/SQLite/内置RCS回环）：留置不重发、单帧漏读、空置后同码新请求、换码立即请求、RCS拒绝后留置不重发且显示原结果、取下重放仅多一次、排队旧帧归属原放置、无SDK借用残留。

按新合同调整：`--camera-chain-reliability`（背压改为不同包裹）、`--camera-chain-closure`、`--mvs-software-flow`、`--sm-original-frame`、`--unread-package-27`（同码前加空置），以及原生 `CameraBarcodeCurrent`、`CameraChainReliabilityUi`、`StationLatencyUi`、`StationResponsiveUi`。依据：旧断言验证“同码逐帧独立请求”，已被本合同否定；用例主题（背压、原图、显示连续性）保持，改用不同包裹。

| 项目 | 状态 |
|---|---|
| 构建（TreatWarningsAsErrors） | NOT_RUN（本会话无 .NET SDK） |
| `--placement-presence-29` 及上述调整用例 | NOT_RUN |
| 原生WPF界面回归 | NOT_RUN |
| 物理MVS实际帧率/漏读率下的1000毫秒间隔 | NOT_RUN，现场待验 |
| 客户RCS、现场托盘小车节拍 | NOT_RUN |

不得以本文件或其他提交的PASS代替上述记录。
