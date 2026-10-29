# Sportmaster RCS 分拣任务服务

当前组件身份见源码项目、extension.build.json及本次同源发布manifest。只支持当前精确组件，历史版本说明见[原件归档](../archive/pre-fresh-baseline-01/README.md)。

WP-IPC-SPORTMASTER-RCS-TASK-01，名称 `SportmasterRcsTaskService`，模块 `sportmaster-rcs-task`，包 `sportmaster.rcs-task`。范围与兼容见[设计](../design/sportmaster-rcs-task.md)、[工作包](../work-packages/WP-IPC-SPORTMASTER-RCS-TASK-01.md)。此组件实现实际HTTP客户端，不提供RCS服务端。现有取格口命令为 `sorter.task.dest_request`，另有周期心跳 `sorter.task.station_heartbeat`，共同POST路径 `/rcs/sorter/api`。

## 实现和职责

[组件源码](../../src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs)实现 `IWcsPort` / `IWcsRequestPreparer`；`RequestDestinationAsync`为首个公开方法。扩展工厂由已有 `ParcelCapabilityFactory`、签名安装、配置CAS和受控重启装配，不在业务或页面里创建HTTP连接。一个组件实例复用于Sportmaster左右投递位，位内顺序、位间并行仍归Application。

新请求T1先保存完整UTF8 JSON、数字requestId、hash和原binding（含目标/参数/合同/包指纹），T2只发送这份原件。数字ID在 `request-id-base/count` 的独占范围内，按实际T1提交高水位分配，独立于较早入场或异步取图顺序。在同一T1事务永久绑定数字ID、Operation、Request及原请求hash；归档不删除归属，相同条码不同实体仍使用不同编号。不同工位、新建数据库或恢复出的并行克隆必须分配不重叠区间；区间不是上游幂等承诺，禁止同时启用原库与其克隆发送。恢复未决原件不分配新ID、不重发。恢复备份时退役来源已登记的全部范围，包括源在备份后可能用掉的编号；新请求在外呼前显示范围耗尽/退役。先停用原根，检查未发实体并受守卫处置，再通过原CAS/受控重启激活外部分配的新不重叠范围。原请求、原编号和原JSON不变；不得删退役表或改原库绕过。

正常回包保留格口、taskId、skuId以及原始可空尺寸重量；不猜测这些字段的单位、不把null变0。NEED_UNPACK保留barcode/skuId/scanQty及每个chute的chuteNo/orderNo/remaining/phase（0是有效剩余量），进入 `UnpackRequired`，本位禁止下一实体。只提供既有“暂停→检查并移除未提交实体”处置，明确记录原因；不把它声称为拆包完成、撤销RCS任务或发起后续RCS命令。

其他明确 `success=false` 保留code/message/args并为WcsRejected。HTTP非2xx、非法/重复JSON字段、缺失字段、requestId/index/command错配不能证明接受/拒绝，保持Unknown。客户端无自动业务重试、无重定向、无虚构查询；模块 `Requirements.Query=false`，核对按钮不会POST原请求。健康投影保留客户端本地生命周期/配置含义；心跳HTTP失败显示未就绪并保留原因，后续新一轮心跳可恢复传输状态。页面仍明确“上游未确认”；未提供心跳回包合同，不把HTTP 2xx视为上游业务确认或原请求核对依据。

响应保留HTTP状态和原始字节base64/SHA256，包含在原StageReply接收账中，继承T3/独立发生/归档/备份恢复链。上限256 KiB；超过上限或读取中断时明确 `HttpResponse.Complete=false`，所留hash只对应已保留前缀，业务结果Unknown。发送前传输失败/超时则保留原请求与结果不明事实。

## 配置和安装

[参数样例](../../config/sportmaster/rcs.parameters.json)全为合成值，不作为现场默认值；默认 `config/sportmaster/business.json` 继续使用原参考组件。真实地址使用binding.Target完整HTTP(S) URI；不得含凭据、query或fragment。source/locale/zone-id/project-id、左右station-code/bcr-code、数字ID独占区间都要显式配置。version固定1、index固定1，每次请求quantity固定1，不接受quantity配置键；指定格口时 `specified-chute=true` 并必须填写 `chute-code`，否则false且报文明确null。

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

## 当前协议与验证边界

RCS的version1、合法可空响应字段、原始UTF8/HTTP字节保全和严格类型校验是外部协议要求，继续保留。内部数据只接受[当前格式](version-compatibility-01.md)，非当前库、配置和包不转换，请使用全新数据目录。当前工具不重验历史介质。原请求、Unknown、UnpackRequired、原始回包及独立资料责任不能因版本变化消失。

request-timeout-ms默认5000毫秒，范围100～30000；连接及响应共用请求期限，调用方更早取消仍有效，心跳独立。超时允许后续独立扫码产生新请求，原Unknown和迟到响应隔离保持。

运行定向验证：

```sh
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --sportmaster-rcs
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --application-architecture
```

验证结果、固定源码及候选身份见[本包证据](evidence/sportmaster-rcs-task/README.md)。真实RCS服务没有在本任务中联调；认证方式、幂等/查询、副作用和尺寸重量单位均未得到进一步合同证据，不补造接口。Windows实际NOT_RUN，真实RCS/设备/现场BLOCKED；本包不能关闭历史D/RV。

编号范围按实际T1的持久高水位分配，入场序号只标识历史顺序；恢复退役及外部划分范围可能减少剩余可用编号，request-id-count不是吞吐承诺。范围耗尽或准备校验失败只持久阻断原实体，Wcs意图/attempt仍未创建；可暂停检查并本地处置后再受控修改配置，不使输入worker崩溃。


并发整合补充：请求数字ID在准备时必须高于本Application库原始T1（含归档）的已分配上界。重配低基值会在发送前阻断本件，WCS尝试为零；暂停、维护认证并检查实体后按原弃置/配置CAS流程处理。重启和备份恢复从原件重建上界，原Prepared/Unknown固定原ID/报文，不因基值变更重配。此约束不是跨站全局分配；现场仍须治理各站互不重叠的区间及服务端幂等合同。
