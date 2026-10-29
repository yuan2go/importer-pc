# Sportmaster内置RCS

SM装配内置RCS与MVS软件读码，不安装独立扩展，不要求扩展信任或按候选签署资格。内置实现仍检查配置、目标、原请求、机器身份、管理权限和安装完整性。[框架](../design/framework.md)规定所有权，[MVS](mvs-software-reader-01.md)规定采集来源。

在四类参数共用候选中配置仓ID、生产线ID、供包台编号、真实RCS地址与超时。供包台编号统一用于取格口与心跳，每台相机的唯一相机编码也由同一配置供两条路径使用。RCS地址为空为待配置，不提供“稍后配置”勾选；有地址须完整校验。保存由Application阻断新准入并有界等待在途，资源成功才确认Effective。

每次独立识别发送一次原条码取格口请求，RCS负责业务去重；同码不等于同实体。SM无PLC下发。请求、发送尝试、原始回包、接收提交及业务接受分别持久化；超时和Unknown保留原请求/报文/目标，不盲重发。心跳独立于取格口，不把心跳成功当作请求接受或相机就绪。具体外部字段、码值和响应解析以 `Ipc.Integrations.Rcs` 的当前合同实现及[心跳说明](rcs-heartbeat-01.md)、[超时说明](rcs-timeout-errors-01.md)为准，本轮不修改外部协议。

合成验证入口：`--sportmaster-rcs`、`--audit03-rcs`、`--rcs-heartbeat`、`--rcs-timeout-errors`、`--mvs-software-flow`。它们使用真实HTTP/SQLite边界及合成结果，不代表客户RCS或现场通过。外置SM验证入口和生成外置SM配置的脚本已删除；其他业务的通用扩展机制保留。

原安装/签署指导及证据见[原字节归档](../archive/pre-contract-convergence-02/README.md)。
