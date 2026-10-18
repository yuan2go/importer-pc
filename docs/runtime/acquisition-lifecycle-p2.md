# 原采集身份生命周期P2 · 修复与验证

2026-10-18。基线main `2126ac2c06a0c352d96afbbf13ec1a719c352738`，已合并[PR #22](https://github.com/yuan2go/importer-pc/pull/22)；独立核验了[原P2评审](https://github.com/yuan2go/importer-pc/pull/22#discussion_r4306589275)。运行源码固定`ca35b16a0ef883126b39640c19ea6f840e5a9716`。已创建[PR #23](https://github.com/yuan2go/importer-pc/pull/23)，base=main，OPEN，未合并；独立开发分支`codex/acquisition-lifecycle-p2`，主工作区受限未跟踪资料及其他工作树未改。

[工作包/设计](../work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md) · [检查与源码hash](evidence/acquisition-lifecycle-p2/checks.json) · [原字节](../archive/pre-acquisition-lifecycle-p2/README.md)。本包是参考实现修复，不是旧现场迁移或真实设备验收。REQ/BR/CAPI/D/RV、原SQL/checksum及22既有候选/发布身份不改。

## 独立复核与源码依据

main的`ManualObservationSource.Emit`在回调返回前写入`acquisitions`，超过4096条无条件删除首条；回调false也会挤掉既有记录。`AcquisitionView.CaptureAsync`必须重新查到完整原身份，缺失会抛IOException。新增最小用例直接走这两个生产方法：接受1条、拒收5000条、随后采原图。修复前[实际失败日志](evidence/acquisition-lifecycle-p2/logs/red-before-fix.log)确认`Original acquisition unavailable; latest frame substitution forbidden`，退出134；这证明参考驱动源码缺陷，未外推为真实相机缺陷。

修复位置：

- [ObservationSources.cs](../../src/Ipc.Drivers/ObservationSources.cs)：完整AcquisitionIdentity作键；仅绑定reader采图时登记。回调前观察所有者暂存，false/throw在finally撤销；没有4096/时间淘汰。所有者与活动采图数分别管理；活动调用实际返回前固定记录，失败传播。Stop/重连保留登记，Dispose遇活动采图拒绝销毁并可重试。
- [ParcelTopology.cs](../../src/Ipc.Application.Contracts/ParcelTopology.cs)：`IReaderImageCapture.RetainAcquisition/ReleaseAcquisition`明确所有者协议，驱动保有原资源，业务决定释放时机。精确身份缺失不能通过Retain重造。
- [ParcelRuntime.cs](../../src/Ipc.Application.ParcelFlow/ParcelRuntime.cs)及[ParcelAcquisitions.cs](../../src/Ipc.Application.ParcelFlow/ParcelAcquisitions.cs)：业务准入为作业保有，消费者finally释放观察；重复/无效/业务阻断不会留下无人负责的登记。观察与作业所有者使用不同前缀、独立ID。
- [ParcelImages.cs](../../src/Ipc.Application.ParcelFlow/ParcelImages.cs)：沿用现有安全终态、Unknown/Conflict/Dispatching、活动阶段和原回执屏障，持久ImageReleased后才释放作业所有者、删临时原图。采集结束但主链/上传仍未结时继续保有；不改变原请求或重发规则。

|生命周期|保有与释放|
|---|---|
|回调执行中|暂存观察所有者，允许异步消费者安全取得原身份|
|回调拒收/抛错|撤销本次观察所有者，不改其他观察或作业|
|已入队、尚未处理|由该观察持有；容量由各位有界队列限制|
|业务拒绝/重复/无图像能力|处理结束释放观察，不创建作业持有|
|业务准入|先增加作业持有，随后才能释放观察持有|
|Prepared/Unknown/Conflict/原回执未清偿|保留原作业责任，不因新输入、重连或新epoch删除|
|安全终态|先持久化释放事实，再幂等释放本作业持有|
|采图正在返回|单独固定原记录，最后所有者释放也不能删除活动采图|
|完整排空并Dispose|结束本进程临时资源；重启只恢复原请求/原持久证据，不凭新帧重造旧采集|

实际内存责任受每位队列容量、主链准入及既有MaximumOperations/Images配额限制；没有用无限历史缓存代替原4096限制。未绑定reader采图的旧Supply/文件回放路径不登记采集身份。

## 实际驱动接入核查与兼容

[ParcelCapabilityFactory](../../src/Ipc.Assembly/ParcelCapabilityFactory.cs)的同物理reader路径复用已创建的来源，要求`IReaderImageCapture`，不会另开连接；接口现要求明确保有/释放。未来真实驱动必须在此协议下固定SDK帧句柄或复制原字节，不能保留一个会被SDK覆盖的最新帧指针。设备重连是否能保有旧句柄、缓存不足如何拒收、SDK实际回调/取消/停止行为均需供应商合同和设备证据。

仓库内的[BarcodeSource](../../examples/BarcodeExtension/BarcodeExtension.cs)只提供合成观察/健康，未宣称reader复用采图能力，缺能力绑定会拒绝。[CameraFactory](../../examples/ParcelCapabilitiesExtension/Factories.cs)创建独立[ReferenceImageCamera](../../src/Ipc.Drivers/ReferenceImageCamera.cs)，底层[ReferencePeer](../../src/Ipc.Reference.Protocol/ReferencePeer.cs)只产生合成PNG并校验冻结Acquisition；该路径没有被修复字典，但也不能据此证明真实工业帧关联。没有取得真实工业SDK驱动源码或现场设备，结论保持BLOCKED。

Application.Contracts、composition模块身份及参考能力包模板升至新不可变`1.5.0`；公共DLL精确指纹改变。旧1.4.0包、请求和候选保留，不重签覆盖；新包必须用目标宿主精确合同重建签名。没有数据库迁移、业务配置schema升级或新设备接口。部署前仍须用匹配旧发布清偿旧未结责任、备份，再受控升级；不能换新实现重投旧请求。

## 验证结果

完整原输出见[证据目录](evidence/acquisition-lifecycle-p2/README.md)。SDK固定10.0.402，锁定还原，构建0警告/0错误。

|范围|结果与实际证明|
|---|---|
|定向生命周期|PASS，28项检查：拒收5000、接受超过4096仍保原件、重复释放、身份错配拒绝、回调抛错、真实异步HTTP采图的在途保护/释放、双位慢采图过载、单侧重连、持久释放顺序、丢回执查询及重启原请求恢复|
|既有AA22|PASS，60项断言及完成标记；双位并行/局部共享阻断/原图/配置/迁移|
|既有可靠性20、能力19|PASS，分别50/49条PASS输出，含其完成/资格标记；原图、Unknown、上传策略、数量/身份及恢复守卫|
|既有AC17含ResourceRisks|PASS，实际三Application链路、原请求、资源/停止/持久回执故障恢复|
|旧来源E07 closure|PASS，来源/回放/健康/资源和Stop/Dispose失败所有权；未绑定图像的路径兼容|
|签名扩展|PASS，新1.5.0 TestOnly独立签名、冻结宿主静态检查、实际安装和能力激活；Debug精确合同包不宣称通用于Release候选|
|发布包|PASS，三个portable及一个Sportmaster win-x64候选构建，全部独立ZIP解压逐文件SHA256核验；不发布替换22历史Release|
|发布启动|PASS，三个解压portable实际启动/主链/重启/停止，Sportmaster含双reader；另一次可选能力旅程含独立HTTP接收进程、实传合成PNG、丢回执查询/继续|
|资料校验|整体FAIL，267项原资产错误与22错误集合完全相同，新增错误0；不删除原资产或修改豁免换PASS|

原失败保留：首次测试夹具使用错误WeightSource枚举导致编译失败，修正为现有Disabled；新工作树缺Fixture构建导致旧验证入口启动超时，构建真实Fixture后通过；签名激活初次使用提交前测试DLL，与冻结宿主指纹不同而拒绝，同一源码提交重建宿主后通过。均未删断言、跳过关键验证或放宽精确合同。候选身份及原hash见[candidate-readback](evidence/acquisition-lifecycle-p2/candidate-readback.json)。

## 运行方式与剩余验收

在仓库根目录，使用global.json指定SDK：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --acquisition-lifecycle
```

发布运行沿[22指南](application-architecture-22.md)，解压后`dotnet app/Ipc.Assembly.dll --config app/config --data-root <全新隔离目录>`。本包候选只存本地`.artifacts/p2/`，Git只收源码、说明、原日志及hash/身份，不上传私钥、SDK/客户资料或二进制。

**NOT_RUN**：Windows实际UI、安装/UAC/ACL、设备实际运行。win-x64交叉构建和解压hash不构成Windows验收。

**BLOCKED**：真实SDK帧生命周期/采集关联/多相机重连、真实WCS/PLC合同、现场离场/复位/联锁及验收。Production缺能力仍拒绝业务；参考通过不关闭D/RV，不证明真实工业相机同类缺陷已修复或现场可上线。
