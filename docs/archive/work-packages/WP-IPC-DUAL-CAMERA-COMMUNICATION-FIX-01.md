# WP-IPC-DUAL-CAMERA-COMMUNICATION-FIX-01

## 2026-10-24 首配CI续修裁决

续修基线 main `c2300cdb73bda9192e2e7912b4fee12753b81121`（PR93已合并）。原PR HEAD `b6e9350`、CI合成提交 `bad2734cc49c07fd6077cbcdc590e4817fcff9ec` 与本次基线分别保留身份。[失败CI](https://github.com/yuan2go/importer-pc/actions/runs/39997147826) 的冻结原生首配在 `VerifyParameterUiRefinement` 提取 `device.ip` 时失败，后续参数壳检查未执行；已生成介质不能视为完整交付PASS。本段补充原实现和局部验证的适用范围，不改写原证据。

源码已证实：TCP目录升级为1.0.1后，`StartupConfigurationDraft` 和运行参数的 `LoadStructuredConfiguration` 仍为无版本字段的内置DeviceBinding伪造1.0.0；精确提供者匹配返回空，导致真实首配及运行参数均缺相机字段。这是模型构造回归，非测试应跳过字段。修复方案是两入口共用内置读取器的目录解析，扩展包仍按原精确身份匹配，目录缺失保持阻断；不改协议/字段/配置账或准入条件。必要验证限定Windows原生首配、运行参数壳，以及目录版本变化、缺失和扩展版本保护的回归。设备/SDK/RCS和现场不执行。

2026-10-24，基线 main `165c2ffe353c59e69729ede4c92fcfd827344c9f`。开工无开放PR。独立分支实施；原工作区与未跟踪附件不动，不合并、不发布、不安装、不连接现场设备。

## 编码前裁决

已证实（源码）：`ParcelRuntime.StartAsync` 在Reader启动之后才设置accepting，TCP连接后立即读取，合法首帧可能被误判为入队拒绝而围栏断线。启动期消费者亦无独立业务屏障，不能仅提前设置accepting修复。单Reader连接异常当前使整个Application启动失败；运行中已有按位健康守卫，应继承其隔离。TCP LastFailure仅留在驱动对象，现有健康/日志没有故障阶段和端点证据。SocketException不在管理重连的受控失败分类内。

待隔离复现：启动即时帧、启动失败/取消与排空、协议边界、双位故障隔离、旧Epoch、Pending与当前连接、业务拒绝后继续读码。待现场确认：左右相机IP/端口、型号与实际TCP/SDK选择、客户端角色/UTF-8/LF合同和每行实体语义，路由/下一跳及独占客户端限制。

方案：启动前准备同一有界输入队列，仅开放接收；消费者等待配置及能力启动终态，失败写阻断证据且不创建请求。通信失败按位呈现；严格协议和过载围栏保留。复用健康Source及原RuntimeJournal记录有界、无条码的连接证据和状态转换，管理诊断可追踪DeviceId→Position→Epoch→Sequence→原输入/作业/请求。不增加自动重连、私有握手、应用读超时或源IP绑定。

## 当前规范与替代关系

本包仅替代 `runtime/startup-experience-03.md` 中TCP启动/应用拒绝的当前描述。其用户确认的客户端、严格UTF-8、LF/CRLF、4096字节、安全实体模式继续有效。海康SDK说明只适用于原SDK实现，不把TCP成功解释成SDK可用。架构22、AQA01的左右拓扑、输入失效与原请求责任、PARAM-UX-02及管理模式继续有效。历史版本、D/RV和原证据不改写。实现、只读脚本、验证和现场步骤统一见[运行记录](../runtime/dual-camera-communication-fix-01.md)。

本包风险验证限定于受影响构建、隔离TCP/真实Application/SQLite及相关恢复、配置、RCS回归；不做生产探测和无关视觉/全量压测。静态核查、Windows隔离执行、真实现场验收分别记录。
