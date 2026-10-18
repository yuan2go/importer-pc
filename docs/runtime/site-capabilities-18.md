# 三场地实际能力与可选功能 · 18

基线 main **d3a757d6314a2d690bcfc4e7763cfd4849b97319**，PR17已合并。用户开场“仅supply/inspection、称重/PLC仅预留”的描述对应较旧版本，不能用于当前能力判断。[工作包](../work-packages/WP-IPC-SITE-CAPABILITIES-18.md)与[原字节](../archive/pre-site-capabilities-18/README.md)。最终源SHA、执行日志及状态见[18结果](site-capabilities-18-results.md)。

## 源码能力矩阵

“已实现”下表均指显式参考运行，真实合同/SDK与现场不在此结论内。

|能力|基线d3a757d实际情况|18交付|
|---|---|---|
|Sportmaster 扫码→WCS格口→展示|Application.Sportmaster→ParcelRuntime→IWcsPort→ReferenceWcs→ReferencePeer；实际WPF Workstation/VM已存在|继承完整链，新增可选重量/登录/传图。无PLC端口。|
|DirectPlc 扫码→重量→PLC|Application.DirectPlc→ParcelRuntime/WeightRules→ReferenceWeightSource/ReferencePlc；实际WPF已存在|继承；称重Disabled时不要求重量、不创建秤；无WCS端口，候选不带Integrations DLL。|
|WcsToPlc 扫码→重量→WCS→PLC|Application.WcsToPlc→ParcelStages；WCS接受与原PLC意图同事务，原格口/请求ID进入PLC payload|继承；可选功能进入同一原请求链。|
|三场景登录与身份快照|新ParcelConfiguration/ParcelOperation/ParcelPayload均无登录字段，非已存在隐藏能力|Login绑定可选；组件认证→接纳时冻结OperatorIdentity→WCS/PLC原payload。|
|每场景可选重量|DirectPlc/WcsToPlc支持Disabled/Configured/Measured；Sportmaster明确拒绝非Disabled|Sportmaster解除旧限制，三者共用同实体/时刻/单位/精度/质量/稳定性验证。|
|采图→上传|新场景既无端口/工厂/状态，也无应用调用链；读码来源不是采图能力|IImageCamera/ReferenceImageCamera驱动采图；IImageUploader/ReferenceImageUploader组件传输真实合成PNG字节。分别持久化阶段和原回执。|
|外部模拟服务|ReferencePeer为嵌入式持久模拟接收账，不能等同独立网络服务|同一参考协议可选literal-loopback HTTP；Ipc.Fixture独立进程分别落账WCS/PLC/上传并注入落账后丢回执。|
|Platform|唯一配置修订、校验和迁移执行、资源租约、签名/精确契约加载已有|复用；未添加业务总控或新加载器。|
|构建/组合切换|compositions已有三新业务，构建期Application选择、独立业务数据库与所属页面|复用三选择；装配新增optional-components依赖及1.1.0公共应用契约。功能开关决定运行依赖实例。|
|桌面UI|三场景实际XAML/VM、共享窗口/历史/维护已有|新增当前会话、原作业身份、图像结果、PasswordBox登录/受保护登出与原阶段继续；Sportmaster增加实测参考入口。Windows运行NOT_RUN。|
|真实身份/秤/相机/上传/PLC/WCS|无获授权可执行协议与现场资格|仍BLOCKED；新增抽象接口不表示真实适配器实现。可选登录/采图/上传当前仅内建参考实现，不声明已支持签名真实插件。Production拒绝。|

Supply已有自身模拟Login/Logout/Actor及箱/数量/发送合同；Inspection为独立采样业务。18没有把三场景的包裹规则扩散到这两个旧业务，也未宣称它们获得新图像或秤协议。旧Supply1～5及新场地原迁移1 SQL/checksum保留。

## 唯一配置与可选依赖

默认`config/{sportmaster,direct-plc,wcs-to-plc}/business.json`显式Login=null、Images=null；重量维持各场景原默认。开启全部可选功能的完整合成配置在`config/optional/<application>/business.json`，使用同一个Application构建；不是第二业务引擎。新根从business.json seed；已有根使用configuration.json唯一修订封套。维护必须经StageConfiguration/Pending/受控重启，禁止手改封套或热切Application。

Login=null：不构造认证端口，Operator为空，不伪造“匿名用户已认证”。Login存在：必须经IOperatorAuthenticator认证，失败与不可用分别返回Rejected/Unavailable。参考主体alice/bob、公开证明字符串`SIMULATION_ONLY`仅限Simulation；没有真实认证、角色授权、凭据配置或客户换班批准。证明不写入作业、请求、审计或备份。每次进程启动都没有当前登录会话。

接纳原作业时快照Subject/DisplayName/Authority/SessionId/AuthenticatedAt/Simulation。换人不会重写旧快照；未完成作业不能主动登出。重启后允许同一原主体重新认证以恢复操作界面，原快照仍为旧会话；不同主体不能接管未完成作业。原查询不依赖新会话，也不创建新动作。

Weight.Source=Disabled时无Scale、无重量字段值；Configured为明确配置补全；Measured必须配置唯一Scale并等待同AdmissionId的有效测量，码相同不代替实体身份。单位/小数精度/质量/稳定性/新鲜度不同或零/负值均不进入下游。

Images=null：不构造Camera/Uploader。存在时两绑定必需，MaximumBytes为68～4194304；当前参考媒体只支持image/png。驱动采集有效合成PNG并以相对路径/长度/SHA256冻结原件。组件读取已验证原字节上传，不重新拍摄。成功回执提供原图像身份及reference://上传标识，WCS和PLC冻结相同图像引用与操作员。

默认参考顺序：身份准入→需要时等待重量→启用时采图→上传→本业务WCS/PLC。上传明确拒绝或未知均不继续。尚未采用“上传与PLC并行/事后补传”等现场规则；需要合同另行定型。

参考绑定Kind/Implementation为login/reference-login、camera/reference-camera、upload/reference-upload，Version=1.0.0，Contract=PARCEL-REFERENCE-v1。相机与秤/PLC获取资源租约；多窗/切页不重建端口。认证/上传归Ipc.Components，不把WCS库强加给DirectPlc。

## 独立模拟服务与恢复

WCS/PLC/上传参数可添加`endpoint=http://127.0.0.1:<port>/parcel-reference/`，仅允许该literal loopback、http和精确路径，无重定向/系统代理。Target仍是显式reference://逻辑身份。冻结endpoint与所有参数；既有未决不会转向新地址。无endpoint使用原独立持久目录模拟，Normalize固定peer-root。HTTP服务使用自身--data目录，既不信任客户端路径作为写入位置，也不随客户端备份回滚。

`reply-mode=lose-after-commit`在服务提交账和图像后返回不可用；客户端保存Unknown，只查询原请求。查询无记录不授权重传。image-capture和image-upload分别使用已有T1/T2/原件Flush/T3机制；原件/T3失败由启动本地回放，未经证明不重发。WCS接受→PLC原意图继续原事务约束。传输、协议、业务接受、物理完成分开。

显式Reconcile只查询原阶段，不下发下游。已查明前阶段、从未尝试且当前未暂停的后续阶段可通过ContinuePrepared继续。暂停、过载、原回执责任和未知结果仍各自守卫。曾尝试或重启保守转Unknown的原意图不能通过此命令重发。PLC已接受仍须权威物理完成事实；软件暂停不是安全急停。

Backup恢复集合现在包含images下的PNG/持久采图账及原回执；机级/运行期`.lease`所有权文件不复制，恢复时重新获取。数据库online backup覆盖WAL；COMPLETED.json最后形成。新隔离恢复保持暂停/无登录，检查原图像hash，外部上传账不倒退。未知`.new`原件拒绝启动，原图损坏/丢失保持阻断。

Application.Contracts从1.0.0增加为**1.1.0**，新精确指纹不与旧签名包混用；示例parcel-capabilities新版本1.1.0。没有覆盖原1.0.0签名包或Release。原未决需匹配原发布/原实现hash恢复；不通过重新序列化、换ID、放宽AssemblyRef或替换原目标来“兼容”。可空新字段缺省不序列化，旧已结数据和配置的既有字段保持；原SQL未改。

## 可重复运行

固定.NET10.0.402；`dotnet`应指向该SDK。核心风险及24组合：

```sh
dotnet run --project tests/Ipc.Application.Verification -- . --site-capabilities
dotnet run --project tests/Ipc.Application.Verification -- .
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -m:1 -nr:false
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=DirectPlc -m:1 -nr:false
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=WcsToPlc -m:1 -nr:false
```

三个选择使用独立输出目录，不能同时写同一bin/obj。实际WPF启动方法与尺寸/DPI后验继承[17指南](application-convergence-17.md)；全选配置路径可用`--config config/optional/wcs-to-plc`。当前会话/原作业身份及图像结果常驻状态区；口令输入后清空；未启用登录隐藏登录区。没有从原HTML中复制业务成功逻辑，也没有Windows截图。

冻结提交后，每个application重复以下命令，输出选全新目录：

```sh
dotnet build src/Ipc.Fixture/Ipc.Fixture.csproj -m:1 -nr:false
python3 scripts/package-application.py --application sportmaster --target portable --dotnet <固定dotnet> --output <全新目录>/sportmaster
# 真正解压ZIP到新根后运行原默认链：
python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --output <default.json>
# 三场景均运行：实际登录/重量/采图/HTTP上传、丢回执、进程重启、原查询/显式继续。
python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --optional-features --peer-fixture src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --output <options.json>
```

smoke只在临时隔离目录生成可选配置，不改已校验候选文件。服务绑定随机loopback端口，接收账和字节独立保存；退出停止服务。Windows候选构建与实际Windows客户端是不同验证范围。

## 剩余阻断

Windows客户端交互/截图/DPI/HID/ACL/UAC：NOT_RUN。真实相机SDK、秤校准/实体关联/时间源、WCS格口/认证/幂等/查询、PLC地址/字节序/握手/动作授权/安全联锁、图像格式/大小/采集时点/上传认证/保留/确认语义、客户换班/权限和现场节拍：BLOCKED。当前不声明任何真实协议已支持，不把reference://标识冒充真实URL或物理完成。D/RV不关闭。
