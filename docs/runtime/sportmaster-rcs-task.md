# Sportmaster RCS 分拣任务服务

已整合main `baac260214f910dca9ae4628ba6449b25188e2ac`（PR26/28）。组件最终版本1.0.1；此前1.0.0及4c9d51c候选保持历史身份。

WP-IPC-SPORTMASTER-RCS-TASK-01，名称 `SportmasterRcsTaskService`，模块 `sportmaster-rcs-task`，包 `sportmaster.rcs-task`。范围与兼容见[设计](../design/sportmaster-rcs-task.md)、[工作包](../work-packages/WP-IPC-SPORTMASTER-RCS-TASK-01.md)。此组件实现实际HTTP客户端，不提供RCS服务端。唯一命令为 `sorter.task.dest_request`，POST路径 `/rcs/sorter/api`。

## 实现和职责

[组件源码](../../src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs)实现 `IWcsPort` / `IWcsRequestPreparer`；`RequestDestinationAsync`为首个公开方法。扩展工厂由已有 `ParcelCapabilityFactory`、签名安装、配置CAS和受控重启装配，不在业务或页面里创建HTTP连接。一个组件实例复用于Sportmaster左右投递位，位内顺序、位间并行仍归Application。

新请求T1先保存完整UTF8 JSON、数字requestId、hash和原binding（含目标/参数/合同/包指纹），T2只发送这份原件。数字ID = `request-id-base + 原数据库持久实体历史序号`；`request-id-count`限制可用范围。历史序号不会随归档删除；相同条码的不同实体获得不同编号。不同工位、新建数据库或恢复出的并行克隆必须分配不重叠区间；区间不是上游幂等承诺，禁止同时启用原库与其克隆发送。恢复未决原件不分配新ID、不重发。

正常回包保留格口、taskId、skuId以及原始可空尺寸重量；不猜测这些字段的单位、不把null变0。NEED_UNPACK保留barcode/skuId/scanQty及每个chute的chuteNo/orderNo/remaining/phase（0是有效剩余量），进入 `UnpackRequired`，本位禁止下一实体。只提供既有“暂停→检查并移除未提交实体”处置，明确记录原因；不把它声称为拆包完成、撤销RCS任务或发起后续RCS命令。

其他明确 `success=false` 保留code/message/args并为WcsRejected。HTTP非2xx、非法/重复JSON字段、缺失字段、requestId/index/command错配不能证明接受/拒绝，保持Unknown。客户端无自动业务重试、无重定向、无虚构查询；模块 `Requirements.Query=false`，核对按钮不会POST原请求。只声明客户端本地生命周期/配置就绪，不假造远端健康证明。

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

公共契约1.7.0：新增字段为可省略，原参考请求/回包的null新增字段不改变序列化字节；扩展指纹已改变，旧包不可重签覆盖。Parcel数据库6追加SQL6，SQL1-5/hash原样继承新main的接收责任与原请求核对修复；实际runtime和消费者检查同一版本，旧reader拒读6。DB4/5原件经main既有raw核验可保留责任升级，DB2/3旧未结门禁不改；显式custody-only迁移仍使用5。新UnpackRequired和Unknown不能靠降级清除。

运行定向验证：

```sh
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --sportmaster-rcs
"$DOTNET_HOST_PATH" run --project tests/Ipc.Application.Verification -- . --application-architecture
```

验证结果、固定源码及候选身份见[本包证据](evidence/sportmaster-rcs-task/README.md)。真实RCS服务没有在本任务中联调；认证方式、幂等/查询、副作用和尺寸重量单位均未得到进一步合同证据，不补造接口。Windows实际NOT_RUN，真实RCS/设备/现场BLOCKED；本包不能关闭历史D/RV。

编号范围按不可变历史序号判定；旧历史和既有索引更新可能留有序号空洞，因此request-id-count不是可处理件数承诺。范围耗尽或准备校验失败只持久阻断原实体，Wcs意图/attempt仍未创建；可暂停检查并本地处置后再受控修改配置，不使输入worker崩溃。
