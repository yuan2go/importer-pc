当前配置仅schema3，Parcel数据库仅10，Supply仅6/6；其他格式拒绝并提示使用全新数据目录。程序集版本与包身份以当前项目声明和本次构建清单为准。旧指南原字节及形成时证据索引见[收口前归档](../archive/pre-fresh-baseline-current-guides-01/README.md)，不用于当前操作。

# Application架构22 · 当前运行指南

[现行设计](../design/application-architecture-22.md) · [工作包](../work-packages/WP-IPC-APPLICATION-ARCHITECTURE-22.md)。22原阶段开工main ea881293c888a824f26c9f9fa07352a22786206b及PR20/21属于形成时身份；本页操作已随PR26复核修复更新，当前源码与证据见[交付记录](architecture-quality-audit-02-results.md)。参考装配为独立F-02工程，不是旧现场迁移。Windows实际NOT_RUN；真实SDK/认证/WCS/PLC/采集关联/离场复位及现场BLOCKED。软件暂停不等于安全急停。

## 构建、选装与运行

固定global.json SDK10.0.402和已锁定NuGet。三个主流程仍构建/打包选择，安装后不能任意切换业务：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --application-architecture
python3 scripts/package-application.py --application sportmaster --target portable --output /new/immutable/candidate --dotnet /pinned/dotnet
# ZIP解压后启动；所有所属页面/布局依同一Application实例
dotnet /new/unpacked/app/Ipc.Assembly.dll --config /new/unpacked/app/config --data-root /new/isolated/runtime
```

DirectPlc、WcsToPlc使用相同脚本分别--application direct-plc/wcs-to-plc。Windows候选--target win-x64包含原有WPF页面和受保护安装/部署脚本；交叉构建不等于Windows运行。DirectPlc完整UI按批准延期；Sportmaster已有双位结果及共享历史/维护页，不新增图片区域。portable包只含所选业务控制台闭包；DirectPlc不带Ipc.Integrations。

默认schema3配置为config/{application}/business.json。Sportmaster显式左右两个reader和投递位，两个独立主链；DirectPlc/WcsToPlc一个main顺序位。config/optional/{application}/business.json为完整认证/实测/采图/上传可选配置：Sportmaster左右分别绑定独立秤和复用各自reader的采集，服务组件可共享。启用秤、上传或认证但缺失/失效必须拒绝；不回退关闭或模拟成功。启用Production须独立真实资格及真实提供者，模拟配置不能直接改Mode成为生产。

## 完整拓扑配置

ParcelConfiguration.SchemaVersion=3，Topology.Readers是DeviceBinding数组（稳定DeviceId、Driver、Role=parcel-reader、Revision）；Topology.Positions声明固定Id及ReaderDeviceId，可显式CameraInstanceId、ScaleInstanceId映射CapabilityInstances。Sportmaster必须left/right及两台reader；其他两个必须main和一台reader。缺失、重复、非法共享、类型错误、禁用和矛盾库存均拒绝。Reader/Scale/Images.Camera字段表示第一位角色绑定，必须保持一致，不能用它们覆盖集合。

Images.Camera启用采图；Images.Uploader=null为仅取图。有Uploader时Strategy=BeforeMain/Attached分别要求前置地址或独立后台责任；若协议要求图片地址，禁止仅取图或Attached。仅取图原件保留到主链终态/安全取消；未知采集/上传保留原件及独立接收责任。MaximumBytes、TotalBytes、MaximumPending控制临时原件容量，无长期图库或预览服务。

Camera.Target等于本位Reader.DeviceId表示复用同物理相机；reader须实现IReaderImageCapture并返回受控原采集门面，拒绝另开连接。签名相机模块须与reader属于同一精确包；不允许借另一位设备。独立相机Target为独立设备身份。schema3每张返回图必须带与原AcquisitionIdentity完全相同的证据，不能取“最近图”。参考manual源的原采集登记按[采集生命周期P2](../work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md)管理，不按固定条数逐出在途记录，仍只有合成图；真实SDK关联仍待验收。

Platform.ObservationSession为每个reader固定epoch、来源sequence和健康，旧注册回调被围栏拒绝；每个秤使用IControlledWeightSource的注册epoch/sequence。重量还需原AdmissionId、绑定实例/设备、稳定性、质量、单位/精度和期限。无重量不是0。PLC语义和握手归驱动，WCS字段、单位、缺省操作人/认证和响应转换归组件，Platform不规定现场报文。

## 实际应用接口及控制台

每行snapshot读取不可变ParcelSnapshot（RuntimeId、配置修订、Positions、Devices、Operations、独立主链/采集/上传事实及可省略Inputs）。Inputs每位最多一项，包含PositionId、原Observation、Outcome（Admitted/Rejected/Blocked/Duplicate）、Reason、OperationId和RecordedAt；原事实使用既有parcel_audit的InputObserved事件持久化，按持久seq恢复，不新增SQL或第二账。来源注册/epoch/sequence先隔离；旧会话、旧序号只审计不覆盖当前观察。同一有效会话的新输入因排队过期须持久化本次拒绝，不能沿用上一件已终结格口；不创建外部请求。观察失败不制造Operation；旧主链已终结时不再把旧成功当本次结果，未决责任继续受守卫。Positions.Admit只表示软件准入守卫，不能当离场传感器/物理许可。主业务结果、可否下一件、后台是否清偿分别投影。PLC接受但未获权威PhysicalCompleted时，顺序位拒绝下一件；Sportmaster终点是有效格口结果，不发送PLC。

每条JSON使用唯一Id；应用命令ExpectedRevision取提交时应用Revision，配置草稿与当前确认账必须保留加载时配置Revision及运行实例，不能由自动刷新改写其检查身份。合成左侧输入示例：

```json
{"Id":"11111111111111111111111111111111","ExpectedRevision":0,"Kind":"EmitReferenceAt","Owner":"synthetic-parcel-1","Value":"{\"PositionId\":\"left\",\"Code\":\"000000917\"}"}
```

右侧用right且新Admission/Source身份。相同条码不代表同一实体。EmitReference仅明确投递到第一个配置位，不能用于证明双位；EmitReferenceAt用于定位输入。非Simulation不接受合成输入。

IParcelApplication.EmitReferenceAt(positionId, admissionId, code, sourceEventId)和ReconnectDeviceAsync(deviceId)为实际契约，CLI对应EmitReferenceAt/ReconnectDevice。单侧重连要求软件暂停和维护认证，只替换该reader注册，不改另一位epoch或重发原请求。操作员认证走已选组件，管理授权走当前管理模式；有未结责任时保护原操作员，不在切页/换班时篡改原件。其他Pause/Resume/Reconcile/ContinuePrepared/ContinueUpload/AbandonUnsubmitted/Backup保持原守卫。

StageConfiguration使用Configuration.Revision及完整ParcelConfiguration，保存PendingRestart；原Effective不变。主链或外部动作未结/未知时激活拒绝；仅当本件外部业务已终结、每个缺raw发生均有已Committed的原Query权威依据时，可在保留独立资料责任的条件下过渡配置。该例外不释放原图、不归档作业、不退休原包；无依据的缺raw仍阻激活。二进制/设备/拓扑/参数一次修订受控重启生效，不给扩展另建配置权威。WPF维护页提供schema3结构化草稿，完整保留reader、投递位、每位秤/相机及共享能力库存、Extensions和Retention；已安装提供者按精确身份选择，局部校验与完整候选校验分开。高级JSON与表单须显式加载草稿，不被轮询或另一入口隐式覆盖；保存仍提交完整候选及加载时修订CAS，禁止降版/丢失右位。非schema3配置拒绝保存。维护的“结构化拓扑与策略”展示reader/位/角色绑定及全局策略，“能力库存与参数”编辑提供者及参数；“完整候选配置（高级）”默认只读，需显式从表单生成并切换，或把当前高级JSON加载回表单，沿用原草稿CAS。重新读取须显式替换本窗草稿。

## 当前格式与原件

从空数据目录一次创建Parcel 10基线。启动、只读检查、扩展消费者、备份及恢复只接受当前格式和基线哈希；缺失回包身份索引拒绝，不补写历史。当前配置schema3必须包含完整拓扑；发布登记仅schema2并核对实际业务程序集，源码开发入口不冒充正式发布。

当前格式的备份经应用BackupAsync取得完整配置、一致SQLite快照、原回执、尚存原图及完成清单。恢复到全新隔离目录并暂停，保留机器来源、退役围栏和防双活。Unknown保持原报文、原目标、原实现及独立责任，不自动重发。WAL、synchronous=FULL和完整性校验保持。

公共DLL变化后以实际Release重新编译业务、驱动与扩展，产生新的不可变包身份。完整包发布和当前格式覆盖安装仍须校验签名、清单、同源程序集及所有权。没有旧格式迁移或旧配置激活入口。

## 排空与边界

每位一个QueueCapacity有限队列和消费者，位内仅一个主业务未结实体；左右慢调用互不占输入消费者。共享短事务锁保护SQLite和快照，外呼释放锁；后台上传数量还受原件配额限制。位故障隔离，共享WCS/存储/全局暂停和总原图限额维持共享阻断；过载记录位及设备，重启不清空责任。

部分构造失败由工厂接管已构造资源，部分启动失败由runtime接管返回端口。Stop停止所有源、排空每位/附加/外呼，再释放共享资源；超时/清理异常保有租约及数据库，允许重复清理。不得把Stop超时或普通软件暂停当作设备已停。

真实离场/复位、共享联锁、SDK关联、WCS/PLC批准合同及Windows实际UI/PowerShell/UAC/ACL仍分别BLOCKED/NOT_RUN。本轮参考逻辑证据不关闭历史D/RV，不声称旧HTML已修复或现场可上线。实际源码、产物与日志身份在本包结果记录。
