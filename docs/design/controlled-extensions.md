> 本包整改权威：[质量闭环设计](architecture-quality-closure-01.md)。22拓扑、19/20可靠性、框架F决策共同继承；本文中的历史实施和版本记录保持形成时范围。

> 22现行拓扑、状态作用域、版本与交付：[Application架构22](../design/application-architecture-22.md)。19/20及PR20/21已闭修复继续有效；冲突处按22增量，原字节见归档。

> 现行三Application职责、公共能力与可选策略：[19职责设计](../design/application-capability-19.md)及[20可靠性增量](../design/capability-reliability-20.md)。本页保留仍有效的基础机制及Supply兼容说明；17/18阶段限制已由19替代。原字节见[归档](../archive/pre-application-capability-19/README.md)。

# CLOSURE-13 受控装配与扩展

承接 main feed6e9c / DELIVERY-12。[前版本原字节](../archive/pre-arch-extension-closure-13/README.md)和12验收不改。实现/验证见[13报告](../runtime/arch-extension-closure-13.md)。Windows实际验收由用户决定统一后置；本轮不寻找VM/runner、不扩OAuth，实际UI仍WINDOWS_NOT_RUN。

## 职责与装配

|所有者|实际路径|边界|
|---|---|---|
|平台机制|Ipc.Platform|生命周期、队列、资源/包租约、签名静态校验、安装/恢复、有限参数schema、受控ALC；不解释供包规则或服务目标业务意义|
|业务公共契约|Ipc.Supply.Contracts|供包意图/回执/查询/参数及应用和维护接口；不依赖SQLite、业务实现、WPF或根容器|
|业务与所属UI|Ipc.Supply；Ipc.Desktop中供包页面|应用拥有状态、T1/T2/T3、唯一接受账、命令守卫和配置修订；页面依IStationRuntime/IStationApplication投影，App是引用具体产品的装配根|
|设备驱动|IObservationExtension；BarcodeExtension|稳定DeviceId、观察/健康/epoch、采集Stop/Dispose；没有WCS服务身份|
|外部集成|IIntegrationExtension/IControlledIntegration；IntegrationExtension|类型化编码、权限事实解码、回执校验、发送、原请求查询、健康、取消/停止/诊断；服务目标与设备分离|
|产品装配|Ipc.Product；Ipc.Assembly的SupplyAssembly/InspectionAssembly|允许引用具体实现；构建选择业务和所属views/layouts，运行共享一个所有者；旧ProductHost与完整供包装配保留|
|非供包验证业务|Ipc.Inspection|真实持久批次/采样/关闭、命令身份/CAS、重启读取及停止；不是客户第二业务，也不使用.bin占位证明|

通用BusinessAssembly和Ipc.Assembly/Program不写Supply/left-right/loopback规则；具体业务注册由构建条件编译的文件提供。供包通过同一个ProductRuntime服务所有页面。inspection产物不含Supply/Adapters/Product；supply组合不带Inspection。组合闭包/页面与精确产物由compositions.json、composition.py、package-business.py/package-reference.py登记；不把业务选装等同任意业务热装。

## 包接受与依赖解析

继续使用.ipcx、独立离线信任、RSA-PSS、签名manifest精确Files及原安装日志。Plan检查全部保留登记和操作，加上不可变identities/{id}/{version}.json；Install/Recover/Repair在同一个管理租约下重验并永久保留首个manifest身份。Uninstalled/retired/中断不释放同ID/version内容绑定；原精确包可重装。失败/损坏原字节保留，不清登记绕过。

产品在预检前注册自己的公共契约程序集并封存白名单；Platform不硬编码业务程序集名。纯类库依赖包可以没有入口模块，不造假模块来满足安装。每个DLL用PE/元数据读取真实AssemblyRef，核对net10.0、程序集名/版本/culture/public-key-token、签名精确依赖闭包和歧义。框架只从锁定Microsoft.NETCore.App运行时目录取得身份，不用System.*前缀或应用TPA白名单。Supply公共契约还需manifest.PublicContracts绑定精确SHA；Platform使用原HostContractSha256。Inspect/安装/提权不加载插件；运行ALC采用同一精确策略，私有依赖从签名闭包唯一解析，禁止P/Invoke和任意原生加载。ALC不是沙箱。

ContractMajor继续1。保留原观察入口、上下文、旧构造签名、StationConfig和原SQL/checksum。仅对12已发布的明确观察契约指纹259CD655…3024E8D允许受控兼容；新集成不得使用此例外。旧供包DTO移到公共契约后由Supply保留TypeForwardedTo。既有请求没有Implementation时明确走原SIMULATION-v1/原Binding/原payload适配，不能借当前目标重解释。

## 两类实例与唯一部署

DeploymentExtensions保留Packages/Instances（设备），可选增加Services/Ports；旧JSON不添null字段。ServiceInstance不携带DeviceId，ServicePortBinding.Target单独保存服务目标；新ServiceState健康投影也不借用设备身份，旧DeviceState形状仅作原专用装配兼容。两类都由签名schema、多实例配额、参数、启停及唯一绑定校验；设备按业务角色，服务按业务端口。供包声明requests端口；其目标与StationConfig.Integration必须一致，由同一部署修订保存和受控重启生效。辅助命名端口可拥有独立服务实例，健康/诊断与参数隔离实际执行。

WPF同PAGE08维护入口生成两类参数表单，显示有效/草稿与部署回执，服务目标编辑不填假设备ID。CLI走同管理实现/应用SaveConfig及ActivateConfig，不另建配置账。参数/二进制默认受控重启；保存不是生效，安装不是激活。CAS、维护身份和原配置版本继续执行；Production缺能力继续NotReady。

## 原请求与所有权

Prepare/T1冻结IntegrationImplementation：Driver、精确Package/Module、Instance、部署修订、配置指纹、公共契约SHA、原Target、超时、业务参考策略和实际参数。Payload/Hash/ID/Binding及原授权不变；参数比较按内容，原不可变请求断言保持。新发送须匹配原生效实现指纹；历史查询和回执校验由Original路由精确能力，改包/改参数/改目标不会重定向。缺能力精确阻断，可继续核心诊断，不换ID、不默认重发。

IntegrationRuntime拥有返回端口及传递依赖包租约，验证返回身份前先接管所有权。发送worker停止与保留原请求核对分开：只要来源Stop/Dispose未终止，应用/Store/Journal和查询能力继续保留。最终释放前关闭新查询，取消/排空整个查询（包含回执校验、T3和查询审计），再完成端口Stop/Dispose；任一任务未终止不释放包、设备、站点或数据租约。重复清理等待原任务；不把超时当停止成功，不回滚业务库掩盖外部事实。

真实SDK/WCS/实体/数量/身份审批及现场资格均不由这些机制替代。只支持明确合成观察和SIMULATION-v1独立本地接收端；没有新增厂商协议、设备控制、微服务、插件市场或图像平台。
