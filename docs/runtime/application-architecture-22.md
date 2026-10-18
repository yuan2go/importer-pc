> 当前版本声明和新发行清单见[版本管理指南](version-compatibility-01.md)；以下22/P2版本均按历史候选身份读取，现行公共契约继承质量闭环01。

# Application架构22 · 运行和兼容指南

[现行设计](../design/application-architecture-22.md) · [工作包](../work-packages/WP-IPC-APPLICATION-ARCHITECTURE-22.md)。开工main ea881293c888a824f26c9f9fa07352a22786206b，继承PR20/21，不自动合并本PR。参考装配为独立F-02工程，不是旧现场迁移。Windows实际NOT_RUN；真实SDK/认证/WCS/PLC/采集关联/离场复位及现场BLOCKED。软件暂停不等于安全急停。

## 构建、选装与运行

固定global.json SDK10.0.402和已锁定NuGet。三个主流程仍构建/打包选择，安装后不能任意切换业务：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --application-architecture
python3 scripts/package-application.py --application sportmaster --target portable --output /new/immutable/candidate --dotnet /pinned/dotnet
# ZIP解压后启动；所有所属页面/布局依同一Application实例
dotnet /new/unpacked/app/Ipc.Assembly.dll --config /new/unpacked/app/config --data-root /new/isolated/runtime
```

DirectPlc、WcsToPlc使用相同脚本分别--application direct-plc/wcs-to-plc。Windows候选--target win-x64包含原有WPF页面和受保护安装/部署脚本；交叉构建不等于Windows运行。本包不新增DirectPlc UI、页面、图片区域或预览能力。portable包只含所选业务控制台闭包；DirectPlc不带Ipc.Integrations。

默认schema3配置为config/{application}/business.json。Sportmaster显式左右两个reader和投递位，两个独立主链；DirectPlc/WcsToPlc一个main顺序位。config/optional/{application}/business.json为完整认证/实测/采图/上传可选配置：Sportmaster左右分别绑定独立秤和复用各自reader的采集，服务组件可共享。启用秤、上传或认证但缺失/失效必须拒绝；不回退关闭或模拟成功。启用Production须独立真实资格及真实提供者，模拟配置不能直接改Mode成为生产。

## 完整拓扑配置

ParcelConfiguration.SchemaVersion=3，Topology.Readers是DeviceBinding数组（稳定DeviceId、Driver、Role=parcel-reader、Revision）；Topology.Positions声明固定Id及ReaderDeviceId，可显式CameraInstanceId、ScaleInstanceId映射CapabilityInstances。Sportmaster必须left/right及两台reader；其他两个必须main和一台reader。缺失、重复、非法共享、类型错误、禁用和矛盾库存均拒绝。旧Reader/Scale/Images.Camera字段为第一个绑定的兼容锚点，必须保持一致，不能用它们覆盖集合。

Images.Camera启用采图；Images.Uploader=null为仅取图。有Uploader时Strategy=BeforeMain/Attached分别要求前置地址或独立后台责任；若协议要求图片地址，禁止仅取图或Attached。仅取图原件保留到主链终态/安全取消；未知采集/上传保留原件及独立接收责任。MaximumBytes、TotalBytes、MaximumPending控制临时原件容量，无长期图库或预览服务。

Camera.Target等于本位Reader.DeviceId表示复用同物理相机；reader须实现IReaderImageCapture并返回受控原采集门面，拒绝另开连接。签名相机模块须与reader属于同一精确包；不允许借另一位设备。独立相机Target为独立设备身份。schema3每张返回图必须带与原AcquisitionIdentity完全相同的证据，不能取“最近图”。参考manual源的原采集登记按[采集生命周期P2](../work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md)管理，不按固定条数逐出在途记录，仍只有合成图；真实SDK关联仍待验收。

Platform.ObservationSession为每个reader固定epoch、来源sequence和健康，旧注册回调被围栏拒绝；每个秤使用IControlledWeightSource的注册epoch/sequence。重量还需原AdmissionId、绑定实例/设备、稳定性、质量、单位/精度和期限。无重量不是0。PLC语义和握手归驱动，WCS字段、单位、缺省操作人/认证和响应转换归组件，Platform不规定现场报文。

## 实际应用接口及控制台

每行snapshot读取不可变ParcelSnapshot（RuntimeId、配置修订、Positions、Devices、Operations、独立主链/采集/上传事实）。Positions.Admit只表示软件准入守卫，不能当离场传感器/物理许可。主业务结果、可否下一件、后台是否清偿分别投影。PLC接受但未获权威PhysicalCompleted时，顺序位拒绝下一件；Sportmaster终点是有效格口结果，不发送PLC。

每条JSON使用唯一Id；有CAS的命令取对应最新Revision。合成左侧输入示例：

```json
{"Id":"11111111111111111111111111111111","ExpectedRevision":0,"Kind":"EmitReferenceAt","Owner":"synthetic-parcel-1","Value":"{\"PositionId\":\"left\",\"Code\":\"000000917\"}"}
```

右侧用right且新Admission/Source身份。相同条码不代表同一实体。旧EmitReference仅明确投递到第一个配置位，不能用于证明双位；新增EmitReferenceAt才是定位输入。非Simulation不接受合成输入。

IParcelApplication.EmitReferenceAt(positionId, admissionId, code, sourceEventId)和ReconnectDeviceAsync(deviceId)为实际契约，CLI对应EmitReferenceAt/ReconnectDevice。单侧重连要求软件暂停和维护认证，只替换该reader注册，不改另一位epoch或重发原请求。认证与维护仍走已选组件；有未结责任时保护原操作员，不在切页/换班时篡改原件。其他Pause/Resume/Reconcile/ContinuePrepared/ContinueUpload/AbandonUnsubmitted/Backup保持原守卫。

StageConfiguration使用Configuration.Revision及完整ParcelConfiguration，保存PendingRestart；原Effective不变。在途未清偿、上传未结/未知或原回执责任未完时激活拒绝。二进制/设备/拓扑/参数一次修订受控重启生效，不给扩展另建配置权威。既有WPF单Reader表单遇schema3明确只读，并在保存方法再次拒绝，禁止降为schema2/丢失右侧或改绑；通过上述完整接口维护。

## 版本升级与原件

P2增量的公共Application.Contracts版本1.5.0；新参考能力包manifest版本1.5.0（22原候选1.4.0保留），仍必须使用目标宿主的精确Platform/Application契约SHA重新构建及独立签名。保留旧1.3.1/更早包与全部ID/version登记，不能覆盖原版本。ContractMajor相同不代表精确契约兼容。包构建/签名/安装/资格沿[扩展指南](../extensions/developer-guide.md)，不允许任意DLL扫描或热换Application。

配置schema1/2保持显式单Reader历史兼容模式，不猜成左右位；默认种子和当前目标使用schema3。已有schema2站必须通过完整拓扑候选/CAS显式迁移。测试中的schema2兼容夹具只保护原机制，不证明新双位。

本宿主数据库版本3，原迁移1/2的SQL/checksum逐字节保留，追加parcel_topology_semantics。不因旧配置仍为schema2就隐瞒新宿主的存储版本。数据库1/2在新增版本前检查所有未结主链、原图/上传、Awaiting/RawDurable回执；任何未结抛LegacyUnsettled且原迁移历史/业务JSON不变。先用匹配旧发布/包/配置清偿，再做完整备份，最后升级。已结原件不回填Position/Reader，也不改原payload/hash/目标/实现身份。

不得用改库、删除原件或强改迁移表作为部署升级步骤。新备份通过应用BackupAsync，包含完整配置、SQLite一致快照、原回执与尚存原图及完成清单；恢复到全新隔离目录，首次启动暂停。原请求、原WCS格口及PLC意图继续同T3事务，Unknown只查询原目标/实现，不盲重发。

## 排空与边界

每位一个QueueCapacity有限队列和消费者，位内仅一个主业务未结实体；左右慢调用互不占输入消费者。共享短事务锁保护SQLite和快照，外呼释放锁；后台上传数量还受原件配额限制。位故障隔离，共享WCS/存储/全局暂停和总原图限额维持共享阻断；过载记录位及设备，重启不清空责任。

部分构造失败由工厂接管已构造资源，部分启动失败由runtime接管返回端口。Stop停止所有源、排空每位/附加/外呼，再释放共享资源；超时/清理异常保有租约及数据库，允许重复清理。不得把Stop超时或普通软件暂停当作设备已停。

真实离场/复位、共享联锁、SDK关联、WCS/PLC批准合同及Windows实际UI/PowerShell/UAC/ACL仍分别BLOCKED/NOT_RUN。本轮参考逻辑证据不关闭历史D/RV，不声称旧HTML已修复或现场可上线。实际源码、产物与日志身份在本包结果记录。
