> 2026-10-20操作裁决：[审查整改](sportmaster-audit-remediation-01.md)固定业务1.4.0 / Contracts1.10.0 / Parcel DB8；recover-legacy-receipt CLI显式目标8，旧API缺省5保持兼容。下文旧版本、候选和验证均保留形成时身份，不能使用旧程序回读DB8。

> 本分支按[UX修复工作包](../work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)更新现行操作；修改前说明的固定Git原字节见[索引](../archive/pre-ux-commercial-fix-01/README.md)。开发验证、最终媒介身份和Windows/现场验收分别记录，本文不宣告后者通过。

# Sportmaster RCS 分拣任务服务

继承已整合main `baac260214f910dca9ae4628ba6449b25188e2ac`（PR26/28）及审查03的协议/编号规则。本分支组件声明1.1.0、业务1.3.0/公共契约1.9.0；原1.0.8、1.0.1、1.0.0及各候选保持历史身份，不能重签覆盖。实际媒介、精确公共DLL和签名身份以本次交付记录为准。

WP-IPC-SPORTMASTER-RCS-TASK-01，名称 `SportmasterRcsTaskService`，模块 `sportmaster-rcs-task`，包 `sportmaster.rcs-task`。范围与兼容见[设计](../design/sportmaster-rcs-task.md)、[工作包](../work-packages/WP-IPC-SPORTMASTER-RCS-TASK-01.md)。此组件实现实际HTTP客户端，不提供RCS服务端。唯一命令为 `sorter.task.dest_request`，POST路径 `/rcs/sorter/api`。

## 实现和职责

[组件源码](../../src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs)实现 `IWcsPort` / `IWcsRequestPreparer`；`RequestDestinationAsync`为首个公开方法。扩展工厂由已有 `ParcelCapabilityFactory`、签名安装、配置CAS和受控重启装配，不在业务或页面里创建HTTP连接。一个组件实例复用于Sportmaster左右投递位，位内顺序、位间并行仍归Application。

新请求T1先保存完整UTF8 JSON、数字requestId、hash和原binding（含目标/参数/合同/包指纹），T2只发送这份原件。数字ID在 `request-id-base/count` 的独占范围内，按实际T1提交高水位分配，独立于较早入场或异步取图顺序。SQL7在同一T1事务永久绑定数字ID、Operation、Request及原请求hash；归档不删除归属，相同条码不同实体仍使用不同编号。不同工位、新建数据库或恢复出的并行克隆必须分配不重叠区间；区间不是上游幂等承诺，禁止同时启用原库与其克隆发送。恢复未决原件不分配新ID、不重发。恢复备份时退役来源已登记的全部范围，包括源在备份后可能用掉的编号；新请求在外呼前显示范围耗尽/退役。先停用原根，检查未发实体并受守卫处置，再通过原CAS/受控重启激活外部分配的新不重叠范围。原请求、原编号和原JSON不变；不得删退役表或改原库绕过。

正常回包保留格口、taskId、skuId以及原始可空尺寸重量；不猜测这些字段的单位、不把null变0。NEED_UNPACK保留barcode/skuId/scanQty及每个chute的chuteNo/orderNo/remaining/phase（0是有效剩余量），进入 `UnpackRequired`，本位禁止下一实体。只提供既有“暂停→检查并移除未提交实体”处置，明确记录原因；不把它声称为拆包完成、撤销RCS任务或发起后续RCS命令。

其他明确 `success=false` 保留code/message/args并为WcsRejected。HTTP非2xx、非法/重复JSON字段、缺失字段、requestId/index/command错配不能证明接受/拒绝，保持Unknown。客户端无自动业务重试、无重定向、无虚构查询；模块 `Requirements.Query=false`，核对按钮不会POST原请求。只声明客户端本地生命周期/配置就绪，页面明确“客户端已启动，上游未确认”，不假造远端健康证明或新增health请求。

响应保留HTTP状态和原始字节base64/SHA256，包含在原StageReply接收账中，继承T3/独立发生/归档/备份恢复链。上限256 KiB；超过上限或读取中断时明确 `HttpResponse.Complete=false`，所留hash只对应已保留前缀，业务结果Unknown。发送前传输失败/超时则保留原请求与结果不明事实。

## 配置和安装

[参数样例](../../config/sportmaster/rcs.parameters.json)全为合成值，不作为现场默认值；默认 `config/sportmaster/business.json` 继续使用原参考组件。真实地址使用binding.Target完整HTTP(S) URI；不得含凭据、query或fragment。source/locale/zone-id/project-id、左右station-code/bcr-code、每次准入quantity和数字ID独占区间都要显式配置。version固定1、index固定1。quantity是获配置的单次准入数量，不由条码/秤推断；指定格口时 `specified-chute=true` 并必须填写 `chute-code`，否则false且报文明确null。

[构建声明](../../src/Ipc.Integrations.SportmasterRcs/extension.build.json)可用于已有独立扩展工具。先提交源码，取得同一冻结宿主旁的Platform及Application.Contracts，再使用独立信任政策和位于Git外的私钥构建签名包，例如：

```sh
python3 scripts/build-extension.py \
  --definition src/Ipc.Integrations.SportmasterRcs/extension.build.json \
  --contract "$RCS_HOST/app/Ipc.Platform.dll" \
  --check-host "$RCS_HOST/app/Ipc.Assembly.dll" \
  --output "$RCS_OUTPUT/sportmaster-rcs-task.ipcx" \
  --key "$RCS_PRIVATE_KEY" --publisher "$RCS_PUBLISHER" \
  --trust "$RCS_TRUST" --dotnet "$DOTNET_HOST_PATH"
```

声明的包模式为Simulation，表示当前只有合成对端验证、无生产资格；HTTP报文与解析是真实协议实现，不会回退模拟成功。Production须有完整合同、真实设备/身份能力、批准信任与精确部署资格；不得把测试根改名后用于Production。

用选装Sportmaster宿主执行已有 `extension-check` / `extension-install`，以安装回执保存精确包身份（Windows按机器政策/UAC/保护目录执行，详见[扩展指南](../extensions/developer-guide.md)）：

```sh
"$DOTNET_HOST_PATH" "$RCS_HOST/app/Ipc.Assembly.dll" \
  --action extension-install --mode Simulation \
  --file "$RCS_OUTPUT/sportmaster-rcs-task.ipcx" --sha256 "$RCS_PACKAGE_SHA256" \
  --extension-root "$RCS_STORE" --extension-trust "$RCS_TRUST" > "$RCS_OUTPUT/installed.json"
python3 scripts/configure-sportmaster-rcs.py \
  --base config/sportmaster/business.json \
  --installation-receipt "$RCS_OUTPUT/installed.json" \
  --parameters "$RCS_PARAMETERS" --target "$RCS_ENDPOINT" \
  --output "$RCS_OUTPUT/business.candidate.json"
```

生成器只写新候选文件，禁止覆盖已有文件；不会联网、验证签名或激活配置。配置目录需按已有本地/Windows政策绑定同一extension store/trust，再用选装宿主 `--action deployment-check --file <candidate>` 核验真实安装schema/包/依赖/资格。运行中通过维护入口ValidateConfiguration/StageConfiguration提交完整候选，受控重启生效；不得覆盖在途配置文件。旧包仍被原件/备份引用时保持安装。

## 兼容与验证边界

公共契约1.8.0已引入纯数字范围描述；1.9.0另增加可省略Inputs观察快照；当前1.10.0增加商业维护契约，不改变RCS原请求语义。组件可按原binding解释配置范围；原1.7.0新增的可省略wire/HTTP/拆包字段保持原序列化。扩展精确指纹改变，旧包不重签。Parcel当前DB8，SQL1～7/hash原样继承；SQL8追加首配/恢复/配置绑定备份读者屏障；SQL7永久绑定数字ID→Operation/Request/hash、登记范围和恢复退役边界。普通DB2～6未结先用匹配发布清偿；窄custody CLI在原件/完整备份/权威Query门禁下显式8，旧API默认5。原Unknown/UnpackRequired及独立资料责任不能靠降级消除。

运行定向验证：

```sh
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --sportmaster-rcs
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --application-architecture
```

验证结果、固定源码及候选身份见[本包证据](evidence/sportmaster-rcs-task/README.md)。真实RCS服务没有在本任务中联调；认证方式、幂等/查询、副作用和尺寸重量单位均未得到进一步合同证据，不补造接口。Windows实际NOT_RUN，真实RCS/设备/现场BLOCKED；本包不能关闭历史D/RV。

编号范围按实际T1的持久高水位分配，入场序号只标识历史顺序；恢复退役及外部划分范围可能减少剩余可用编号，request-id-count不是吞吐承诺。范围耗尽或准备校验失败只持久阻断原实体，Wcs意图/attempt仍未创建；可暂停检查并本地处置后再受控修改配置，不使输入worker崩溃。

并发整合兼容说明：当前CLI recover-legacy-receipt显式迁移至Parcel DB8；内部API默认DB5调用保持兼容。两者均保留永久回复身份、持久Query原件依据和完整备份，未清原raw/图像/包责任；普通启动在任何未来reader标记前拒绝DB2–6未结责任。原SQL1–5和历史发布指令按其固定版本保留。

并发整合补充：请求数字ID在准备时必须高于本Application库原始T1（含归档）的已分配上界。重配低基值会在发送前阻断本件，WCS尝试为零；暂停、维护认证并检查实体后按原弃置/配置CAS流程处理。重启和备份恢复从原件重建上界，原Prepared/Unknown固定原ID/报文，不因基值变更重配。此约束不是跨站全局分配；现场仍须治理各站互不重叠的区间及服务端幂等合同。
