# Sportmaster RCS 分拣任务服务 · 01

2026-10-19用户已确认实现、必要验证、提交推送及main PR。执行基线main `4afe2afacde9e2acab7648fff3440fa3cc331b6e`；PR26并行不合并、不覆盖。命名 `SportmasterRcsTaskService` / `sportmaster-rcs-task`。本增量继承22与质量闭环01，原D/RV、Supply CAPI含义及历史报告不改。

## 组件与事务

新独立集成程序集实现 `IWcsExtension`、`IWcsPort`，由既有签名安装、配置修订、受控重启链装配。HTTP不进入业务/UI/基座。首项只实现POST目标地址上的 `sorter.task.dest_request`；不实现RCS服务器、PLC控制、认证/查询/拆包提交或物理完成。

`IWcsRequestPreparer` 是纯序列化的可选伴随端口：使用已提交实体的持久历史序号和冻结配置，生成数字requestId与完整UTF8 JSON；T1保存原JSON/hash/目标/合同/包后T2只发送原字节。数字ID使用配置的request-id-base加不可删除历史序号，限制于独立配置的request-id-count区间；不同工位/新库/克隆库需分配不重叠范围。恢复原库保持原序号。未提供上游幂等保证，不声称全局恰好一次。

地址由binding.Target明确给出（路径 `/rcs/sorter/api`）。source、locale、zoneId、projectId、每位stationCode/bcrCode、quantity、specifiedChute/chuteCode均属于组件参数；数量为明确配置的每次准入量，不从扫码/重量推导。默认参考配置保持参考协议，真实HTTP需显式选择签名组件。

## 回包与业务

保留HTTP状态、响应原始字节base64/hash及类型化结果。匹配requestId、唯一index=1、command和success；缺失/歧义/非法结构不推断成功。success=true必须有有效格口；保留taskId、skuId与可空尺寸重量。NEED_UNPACK保留barcode、skuId、scanQty、所有chutes（含remaining=0及未知phase原值），进入独立UnpackRequired状态，本位等待现有受守卫的检查移除动作，不自动下一件、不代发拆包命令。其它success=false保留code/message/args为业务拒绝。HTTP错误/超时/断连/非法JSON/关联错误保持Unknown，不重试/重定向/用POST假查询。

共享StageReply增加可省略的目的地结果及HTTP原件，旧参考回执序列化字节不变。新数据库语义5追加迁移，SQL1-4不改；旧未结原件仍在迁移前拒绝，旧宿主拒读新库。公共契约精确指纹改变，旧包不重签覆盖。

## 验证与公开边界

必要基线/定向HTTP夹具覆盖三种结果、空字段、关联/结构/HTTP失败、超时、双位并行、原请求持久化、重启Unknown无重发、拆包本位阻断及历史/迁移；实际签名包装配和选装portable发布启动。Windows仅交叉构建，实际Windows/真实RCS/设备/现场分别NOT_RUN/BLOCKED。测试只用合成标识，不复制用户接口全文或示例业务标识，不上传客户原稿/凭据/真实地址。
