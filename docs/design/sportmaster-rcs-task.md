> 2026-10-20心跳增量：用户新增 `sorter.task.station_heartbeat`，默认5秒、组件参数可改；系统 `StationCode` 与Reader驱动 `bcr-code` 为心跳编码来源。实现/配置/验证边界见[心跳说明](../runtime/rcs-heartbeat-01.md)。下文“首项只实现取格口”保留01形成时范围，不限制本次新增命令；Query仍为false，旧请求原件不改。

# Sportmaster RCS 分拣任务服务 · 01

2026-10-19用户已确认实现、必要验证、提交推送及main PR。开工基线main `4afe2afacde9e2acab7648fff3440fa3cc331b6e`；当时PR26并行，执行中其已由独立授权合入main，本分支整合经过见末段。命名 `SportmasterRcsTaskService` / `sportmaster-rcs-task`。本增量继承22与质量闭环01，原D/RV、Supply CAPI含义及历史报告不改。

## 组件与事务

新独立集成程序集实现 `IWcsExtension`、`IWcsPort`，由既有签名安装、配置修订、受控重启链装配。HTTP不进入业务/UI/基座。首项只实现POST目标地址上的 `sorter.task.dest_request`；不实现RCS服务器、PLC控制、认证/查询/拆包提交或物理完成。

`IWcsRequestPreparer` 是纯序列化的可选伴随端口：使用实际T1准备时的持久编号边界和冻结配置，生成数字requestId与完整UTF8 JSON；T1保存原JSON/hash/目标/合同/包后T2只发送原字节。数字ID在组件声明的request-id-base/count区间内，按已提交T1高水位分配，不能用更早入场序号拒绝合法迟到准备。SQL7的永久唯一Owner账与原WCS请求同事务提交，归档不删除编号归属。恢复备份时退役已登记的全部来源范围（包含源在备份后可能消耗的尾段），新业务必须经原CAS/受控重启取得外部分配的新不重叠范围；旧请求ID/原报文不变。不同工位/新库/克隆库仍须分配不重叠范围并停用旧根。未提供上游幂等保证，不声称全局恰好一次。

地址由binding.Target明确给出（路径 `/rcs/sorter/api`）。source、locale、zoneId、projectId、每位stationCode/bcrCode、quantity、specifiedChute/chuteCode均属于组件参数；数量为明确配置的每次准入量，不从扫码/重量推导。默认参考配置保持参考协议，真实HTTP需显式选择签名组件。

## 回包与业务

保留HTTP状态、响应原始字节base64/hash及类型化结果。匹配requestId、唯一index=1、command和success；缺失/歧义/非法结构不推断成功。success=true必须有有效格口；保留taskId、skuId与可空尺寸重量。NEED_UNPACK保留barcode、skuId、scanQty、所有chutes（含remaining=0及未知phase原值），进入独立UnpackRequired状态，本位等待现有受守卫的检查移除动作，不自动下一件、不代发拆包命令。其它success=false保留code/message/args为业务拒绝。HTTP错误/超时/断连/非法JSON/关联错误保持Unknown，不重试/重定向/用POST假查询。

共享StageReply增加可省略的目的地结果及HTTP原件，旧参考回执序列化字节不变。整合main PR26/28后新数据库语义6追加迁移，SQL1-5原样继承；DB4/5仍按main已获授权的raw核验/原请求核对链保留恢复能力，DB2/3原未结门禁不改；旧宿主拒读6。公共契约精确指纹改变，旧包不重签覆盖。

## 验证与公开边界

必要基线/定向HTTP夹具覆盖三种结果、空字段、关联/结构/HTTP失败、超时、双位并行、原请求持久化、重启Unknown无重发、拆包本位阻断及历史/迁移；实际签名包装配和选装portable发布启动。Windows仅交叉构建，实际Windows/真实RCS/设备/现场分别NOT_RUN/BLOCKED。测试只用合成标识，不复制用户接口全文或示例业务标识，不上传客户原稿/凭据/真实地址。

编号归属在实际T1提交，准备失败不外呼；范围耗尽/退役须暂停检查原实体并沿原配置账切换外部分配的新范围。旧格式数字意图只读预检后回填唯一Owner账，矛盾不先写未来reader标记。范围耗尽或准备校验失败只持久阻断原实体，Wcs意图/attempt仍未创建；可暂停检查并本地处置后再受控修改配置，不使输入worker崩溃。

2026-10-19执行中main推进至baac260，PR26/28已合并；继承其SQL5接收责任、原件查询/缺件保管及发布链。RCS追加SQL6，恢复主投影保持UnpackRequired，不因旧业务拒绝投影释放本位。初轮4c9d51c/组件1.0.0为历史候选；整合组件采用新不可变1.0.1，未改写旧包。

并发整合兼容说明：当前CLI recover-legacy-receipt显式迁移至Parcel DB6；内部API默认DB5调用保持兼容。两者均保留永久回复身份、持久Query原件依据和完整备份，未清原raw/图像/包责任；普通启动在任何未来reader标记前拒绝DB2–5未结责任。原SQL1–5和历史发布指令按其固定版本保留。

审查03执行增量：当前契约ABI1.8.0、业务1.2.7及DB7；SQL1～6原字节保留。恢复/编号语义变化要求旧DB6读者拒读7。普通DB2～6未结先匹配发布清偿；专用custody CLI选择7，旧API默认5。原RCS合同未新增Query或人工成功清责能力。
