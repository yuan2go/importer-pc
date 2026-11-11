# Sportmaster RCS 取格口合同

SM 使用内置 RCS HTTP 客户端，不配置请求编号范围或本地模拟提供者。格口分配方式由左右位共用，默认由 RCS 分配（`specifiedChute=false`、`chuteCode=null`）；选择指定格口时必填完整格口编码，新请求发送布尔 `true` 和字符串编码，实际格口以 RCS 返回为准。无可用格口策略通过 `noDestToExp` 冻结到原报文。

Application 在冻结请求前按 Unix 微秒时间及永久编号下限分配 requestId，taskId 使用同一数字。编号所有权与请求同事务持久化，重启、归档、清理及恢复不复用。source 按实际来源为 BAR、MANUAL 或 PDA；人工输入不依赖相机在线或已配置，不伪造物理身份。相机编码与心跳映射共用配置。

一次放置只创建一次请求，重复观察不重发。原请求的目标、报文、参数、编号及原始回执是业务事实，不用当前配置重写。结果未知保留 Unknown；确认离位且调用及回执责任收口后转历史保管，合法下一件使用独立请求。没有权威查询合同，不盲重发或用 POST 伪造查询。

响应须核对原 requestId、command、唯一结果 index 和身份。真实业务拒绝显示原消息；超时、错配、无效响应或成功但格口为空保持未确认。心跳状态只提示小车状态，不代表本件业务接受或物理完成。

完整操作及测试入口见[内置 RCS](../runtime/sportmaster-rcs-task.md)和[心跳](../runtime/rcs-heartbeat-01.md)。历史版本、迁移及验证仅保留于 Git 和 archive，不作为当前格式或现场已通过的证据。
