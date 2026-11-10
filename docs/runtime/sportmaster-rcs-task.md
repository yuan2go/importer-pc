# Sportmaster内置RCS

SM 装配内置 RCS、海康原生识码和 MVS 软件读码。内置实现仍检查配置、目标、原请求、机器身份、管理权限和安装完整性。[框架](../design/framework.md)规定所有权，[MVS](mvs-software-reader-01.md)规定采集来源。

在四类参数共用候选中配置仓ID、生产线ID、供包台编号、真实RCS地址与超时。供包台编号统一用于取格口与心跳，每台相机的唯一相机编码也由同一配置供两条路径使用。RCS地址为空为待配置，不提供“稍后配置”勾选；有地址须完整校验。保存由Application阻断新准入并有界等待在途，资源成功才确认Effective。

每次新放置发送一次原条码取格口请求，持续同码不重复发送；RCS负责业务去重，同码不等于同实体。SM无PLC下发。请求、发送尝试、原始回包、接收提交及业务接受分别持久化；超时和Unknown保留原请求/报文/目标，不盲重发。确认离位且实际调用结束、回执已持久化或明确无回包后，Unknown 进入历史保管。原请求可从“记录”核对，后续新配置和新作业按各自守卫执行。心跳独立于取格口，不把心跳成功当作请求接受或相机就绪。具体外部字段、码值和响应解析以 `Ipc.Integrations.SportmasterRcs` 的当前合同实现及[心跳说明](rcs-heartbeat-01.md)、[超时说明](../archive/runtime/rcs-timeout-errors-01.md)为准，本轮不修改外部协议。

SM 不配置请求编号范围，也不提供指定格口或本地模拟连接方式。新请求固定为普通取格口（specifiedChute=false、chuteCode=null）；协议 requestId 由 Application 在冻结请求前按 Unix 微秒时间及永久编号下限分配，不受 10000 上限限制。原请求的编号所有权、报文哈希、目标和 Unknown 守卫继续有效；旧冻结报文仅按原事实校验，保存新配置时清除废弃字段，不重写历史请求。RCS 地址仍须明确填写，独立测试服务器不是产品中的模拟提供者。

合成验证入口：`--sm-automatic-rcs`、`--sportmaster-rcs`、`--audit03-rcs`、`--rcs-heartbeat`、`--rcs-timeout-errors`、`--mvs-software-flow`。它们使用真实HTTP/SQLite边界及合成结果，不代表客户RCS或现场通过。外置SM验证入口和生成外置SM配置的脚本已删除；其他业务的通用扩展机制保留。

原安装/签署指导及证据见原字节归档。
