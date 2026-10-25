# P0-STABILITY-01 本次验证

基线 `3a5b1d13ec5969213db35ed43d72a5aecd437777`。本页只记录本轮实际执行；尚未执行项保持 NOT_RUN，不继承历史 PASS。

## ST01 问题卡

确认：八组合夹具先于产品修改运行；mask 1/2/3（RCS启用且左/右/双相机延后）在联合校验、离线保存及Application启动复现缺少驱动bcr-code异常。首次夹具存在种子身份不一致和缺Windows运行政策的独立失败，修正夹具后再次记录，不将它们当产品缺陷。

修复：共用映射过滤待配置和明确禁用的实例；零活动读码器不发送心跳并保持未就绪。原业务报文生成、Unknown责任及一次扫码一次请求不改。

验收：八组合字段/联合检查/保存/隔离Application启动全部PASS；零活动读码器无心跳。原`--parameter-guidance-startup`、`--rcs-heartbeat`通过。新增隔离Application→TCP→RCS回归验证三次扫码（含相同条码）恰好三次原条码请求，原SQLite冻结Body与HTTP原文相等，旧quantity=7冻结报文逐字节保持。原`--sportmaster-rcs`组件协议/冻结报文断言通过，随后在受保护启动门禁因缺维护安装runtime policy受阻，该完整段NOT_RUN；旧测试未修改。

测试装配使用真实Application/SQLite/TCP/RCS组件及本地桩，不安装机器租约政策，不替代正式装配资格。新增扫码夹具首次错误读取正常投影的隐藏字段；改为只读原SQLite后通过，未放宽产品脱敏。

预留SM1.14.0（避开并行PR108的1.13.0）、RCS1.3.3；旧内置1.3.2候选可读取，新候选1.3.3，原请求字节/实现hash守卫不变。Contracts1.18.0/DB9不变。原始日志保留本地，公开回执无账户路径、凭据或现场数据。

残余：全部读码器延后时不发心跳待合同确认。正式Production资格/真实设备/RCS、完整24h/72h：NOT_RUN。
