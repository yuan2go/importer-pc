> RCS增量01：[Sportmaster RCS分拣任务服务](../runtime/sportmaster-rcs-task.md)。首项真实HTTP协议组件与参考夹具验证分别记录；公共契约1.7.0、Parcel数据库5，旧未结升级屏障继续有效。旧1.6.0/SQL4与无真实协议的说明按形成时读取，原字节见[归档](../archive/pre-sportmaster-rcs-task/README.md)。Windows实际NOT_RUN，真实RCS/设备/现场BLOCKED。

> PR24 R03规则已实施：schema1/2/3在任何All/Bound去重之前校验原库存唯一性、镜像内容一致性及精确包闭包；歧义原件明确阻卸载。见[固定源码/验证边界](../runtime/architecture-quality-closure-01-r03-results.md)，旧包/候选身份不变。

> 路径边界：业务SQL及状态语义仍由各业务维护读者拥有。PR24整改由装配ExtensionConsumerReaders分派已知Supply/Parcel消费者；不认识、未来、损坏或身份不一致的根必须阻断。新Application的安装恢复/消费者/备份及安全卸载见[质量闭环01运行指南](../runtime/architecture-quality-closure-01.md)。不得把错误格式当零引用；原包身份不放宽。

> 采集生命周期P2：复用reader采图的提供者须实现`IReaderImageCapture`新增的RetainAcquisition/ReleaseAcquisition所有者协议，且回调前暂存观察所有者、拒收/异常撤销，Stop/重连保有在途，采图实际返回前不得释放底层资源。业务拥有释放时机，不得按缓存条数或时间逐出。Application.Contracts及参考能力包进入新不可变1.5.0，须对新宿主精确契约重新构建/签名；历史1.4.0包与请求不覆盖。详情见[本次工作包](../work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md)。

> 22现行拓扑、状态作用域、版本与交付：[Application架构22](../design/application-architecture-22.md)。19/20及PR20/21已闭修复继续有效；冲突处按22增量，原字节见归档。

> 现行三Application职责、公共能力与可选策略：[19职责设计](../design/application-capability-19.md)及[20可靠性增量](../design/capability-reliability-20.md)。本页保留仍有效的基础机制及Supply兼容说明；17/18阶段限制已由19替代。原字节见[归档](../archive/pre-application-capability-19/README.md)。

# 受控扩展开发、构建与安装

Supply兼容扩展指南对应[UI-MAINTAINABILITY-16](../work-packages/WP-IPC-UI-MAINTAINABILITY-16.md)。[原15及13指南原字节](../archive/pre-ui-maintainability-16/docs/extensions/developer-guide.md.txt)保留历史命令与版本，当前操作以本页和[运行指南](../runtime/operations.md)为准。Storage/MinimumReader=5/5、ExtensionContract=1；本包参考扩展使用新不可变版本1.0.4。原1.0.0～1.0.3包、身份登记、历史宿主和请求保持，不重签覆盖。

设备扩展实现`Ipc.Platform.IObservationExtension`，只构造来源；受控Start后开始观察/可靠健康确认，Stop/Dispose必须涵盖回调和在途任务。服务扩展实现`Ipc.Supply.IIntegrationExtension`，返回`IControlledIntegration`，拥有编码、原目标执行/查询、权威验回、健康、取消、超时和完整停止。设备有DeviceId及业务Role；服务有独立实例/端口/目标，不造设备ID。两者都不拥有SQLite、业务状态、页面、根容器或第二接受账。契约和精确AssemblyRef/依赖闭包由冻结宿主预检；ALC不是代码沙箱。

## 一个独立工程的声明

构建器接受独立Git仓库中的声明，不要求在本仓库增加代码或修改脚本。将以下文件和源码、NuGet锁、SDK/构建配置共同提交。路径以该插件Git仓库根为基准：

```json
{
  "SchemaVersion": 1,
  "Project": "src/Reader/Reader.csproj",
  "SourceRoots": ["src/Reader", "packaging"],
  "BuildInputs": ["Directory.Build.props", "global.json", "NuGet.Config"],
  "Manifest": "packaging/manifest.template.json",
  "Modules": "packaging/modules.json",
  "ParameterSchemas": {"reader": "packaging/reader.parameters.json"},
  "Content": [
    {"Root": "build", "Source": "Example.Reader.dll", "Path": "Example.Reader.dll"},
    {"Root": "source", "Source": "packaging/readme.txt", "Path": "readme.txt"}
  ],
  "PublicContracts": []
}
```

声明自身必须处于SourceRoots或BuildInputs中。`SourceRoots:["."]`可选整个独立仓库；不要把输出/密钥置入已声明源码范围。源范围的未提交改动或未跟踪输入、浅历史、链接/越界路径拒绝。只复制声明范围的Git已跟踪普通文件到临时隔离根，再locked restore/build；未声明的外部构建文件不会继承。构建代码是开发者获授权的可信代码，构建器不是恶意MSBuild的沙箱。

`Manifest`必须明确包含SchemaVersion=1、Id、三段Version、Runtime=net10.0、Rid、ContractMajor=1、Mode、Dependencies和Prerequisites；不填写生成的SourceCommit、Publisher、Files、模块、哈希。`Dependencies`为完整精确`Id/Version/ManifestSha256`直接依赖，不能填版本范围。`Modules`是`Id/Assembly/EntryType/Kind/MaximumInstances`数组；每个模块映射独立schema文件，纯依赖类库可用空数组和空映射。`Content`逐项声明编译产物或提交的源文件及包内路径；私有DLL不能漏包，宿主公共DLL不能打进插件。跨包依赖使用签名的精确包，不复制另一包的实现冒充自己的内容。

参数沿现有有限schema：string/integer/boolean/choice/string-list/secret-reference，分组、标签、单位、范围/长度/集合上限、默认、条件必填、只读、说明；EditRole=Maintenance，ApplyLevel=Restart。敏感参数仅存`credential:名称`引用，不能在参数或包中写密码。宿主直接从schema生成表单，不执行包内XAML/脚本。完整可复制声明见[观察示例](../../examples/BarcodeExtension/extension.build.json)、[服务示例](../../examples/IntegrationExtension/extension.build.json)，项目只引用冻结公共契约；不加入宿主ProjectReference。

## 构建、静态检查与签名

先取得本次冻结宿主（当前`Ipc.Product.dll`含`extension-check`静态入口），使用目标宿主相邻的`Ipc.Platform.dll`；集成还需相邻`Ipc.Supply.Contracts.dll`，在PublicContracts声明其程序集名。独立私钥放构建者私有目录，信任政策从独立渠道取得，SchemaVersion=1，Publishers为Id/PublicKeyPem/TestOnly；包内公钥不会自动入信。Simulation接受明确TestOnly信任，Production拒绝测试根；通过静态检查不授予生产能力。

```sh
python3 scripts/build-extension.py \
  --definition /independent/reader/packaging/extension.build.json \
  --contract /frozen/app/Ipc.Platform.dll \
  --output /new-packages/example.reader-2.0.0.ipcx \
  --version 2.0.0 --publisher approved-publisher \
  --key /private/build/publisher.pem --trust /independent/trust.json \
  --dotnet /pinned/dotnet
```

有包依赖时，为完整闭包重复指定`--dependency-package /original/dependency.ipcx`，顺序不限。`--kind observation|integration`保留兼容快捷入口，只选择对应示例的声明，不再内嵌两套模块/schema/打包规则；不传kind和definition时选观察示例。参考发布组合从`config/compositions.json`传入1.0.4。

构建器记录声明/源码字节hash与最后实际输入提交；从隔离源码确定性编译，绑定精确公共DLL指纹，签名后调用同冻结宿主`extension-check`。该入口复用`ExtensionPackages.Inspect/ExtractVerified`验证签名、配额、实际托管元数据/入口、依赖闭包和schema，仅在临时目录校验依赖，不注册/安装包、不修改信任、不启动业务或加载插件。多余、缺少、冲突、循环、变更的依赖拒绝。宿主全文件前后hash与插件输入再次核验后，才独占发布完整`.ipcx`及`.identity.json`。失败/取消不产生成功包；原输出存在就拒绝覆盖。RSA-PSS签名随机；可复现性比较原始manifest和payload，不要求ZIP签名字节一致。

可单独执行同一静态验收（dependency-files是签名依赖包绝对路径JSON数组）：

```sh
dotnet /frozen/app/Ipc.Product.dll --action extension-check --mode Simulation \
  --file /new-packages/example.reader-2.0.0.ipcx --extension-trust /independent/trust.json \
  --dependency-files /independent/dependencies.json
```

此检查不替代安装时永久ID/version登记、机器策略、ACL或实例启动。macOS交叉打包win-x64自包含候选时，不能执行Windows自包含宿主。为构建器提供`--check-host /frozen-portable/app/Ipc.Product.dll`，或为package-reference.py提供`--extension-check-host`（PowerShell为`-ExtensionCheckHost`）。检查器必须来自冻结可运行宿主，Ipc.Platform与全部声明公共契约逐字节匹配目标，且两份宿主前后不变；任何差异拒绝，不放宽目标合同指纹。旧冻结宿主没有新静态入口时用其原版构建器/原包，不混用不同公共DLL冒称兼容。公共DLL指纹变化即便契约主版本不变也要新不可变包版本；T1原请求固定原实现、合同及目标，缺原能力精确阻塞，不能通过换包/换ID重发解除。

## 安装、配置、激活和失败保旧

portable合成开发使用独立store与独立信任，CLI和WPF调用同一个ExtensionStore：

```sh
dotnet /frozen/app/Ipc.Product.dll --action extension-inspect --mode Simulation --file /new-packages/example.reader-2.0.0.ipcx --extension-root /reference/store --extension-trust /independent/trust.json
dotnet /frozen/app/Ipc.Product.dll --action extension-install --mode Simulation --file /new-packages/example.reader-2.0.0.ipcx --sha256 ORIGINAL_UPPERCASE_PACKAGE_SHA256 --extension-root /reference/store --extension-trust /independent/trust.json
```

Windows安装使用受保护Program Files发布与维护配置的ProgramData/指定账户、机器租约、独立信任；`protect-release.ps1`、`provision-runtime.ps1`和`provision-extensions.ps1`及受限runas入口见运行指南。Windows不允许自选store/trust绕过机器策略。UAC取消、ACL拒绝、签名/路径变化保持失败，不伪称已安装。静态检查不需要安装权限，不提供提权执行插件入口。

1. 校验Installed回执。安装不等于激活；保留原操作ID、包SHA及身份登记。
2. 在组件页选择精确包加入本窗统一部署草稿；选模块创建设备或服务，配置参数并与当前Effective对照。设备绑定真实稳定DeviceId/Role；服务绑定独立实例/端口和固定目标。
3. 预览差异并定位字段错误。普通维护、组件参数和角色/端口共享一个草稿；未完整草稿仍可切模块/新建，保存前必须完整校验。
4. 维护身份有效时SaveConfig提交ExpectedRevision/CAS，读取已保存修订；显式选择该修订ActivateConfig。Validated、PendingActivation、Effective、ActivationFailed分别显示，保存不冒充有效。
5. Pending要求受控停止/重启；所有实际资源/精确包/参数初始化成功才成为Effective。构造、启动、Stop/Dispose失败保留旧有效修订及相关资源，完成停止后按回执修复/恢复。查询或T3未终止不能释放原包/依赖/数据租约。

控制台同样先snapshot取当前revision，发送`SaveConfig`（Value为完整StationConfig JSON）、输入`configs`读取已保存修订数组，再显式`ActivateConfig`（OwnerId为该原修订ID）。推荐直接使用组件页或下述自动旅程，避免手工拼接猜测修订身份：

```sh
python3 scripts/smoke-extension.py --release /new-unpacked-candidate --dotnet /pinned/dotnet --output /new-evidence/extension.json
```

此脚本验证两类包安装、实际来源/服务、SQLite/接收账和停止；合成新站种子只证明首次部署，不冒称已有站配置升级。旧Effective升级使用运行指南及`verify-operation-upgrade.py`的匹配旧宿主/原包/过渡修订旅程。

## 恢复、卸载与验证选择

Prepared/FilesVerified/Published/Registered中断沿原操作恢复，损坏原件移入damaged保留，精确包可修复重装。ID/version跨Uninstalled、retired和中断永久绑定首个manifest；新内容用新版本，不能删除登记避开。先在部署草稿停用/解绑/移除包并受控重启，再卸载；应用重新检查反向依赖、Effective/Pending、全部未决请求/独立接收发生、离线消费者和真实包租约。LegacyConsumersUnverified继续拒绝卸载，需要真实原根/备份盘点及批准。

定向验证先执行`dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --help`。默认`--reconciliation`包含H04/A01/E07/E08/E11/E12/AC14/AC15和原AC/进程基线；`--architecture`专门验证身份/实际AssemblyRef/独立扩展与装配，不在默认范围；`--maintainability`与`--health-profile`为16风险和健康写量测量的显式范围。只按改动运行必要选择器，不循环全量替代实机。构建工具边界测试：`python3 -m unittest discover -s scripts -p 'test_extension_build.py' -v`；其编译/进程失败注入不冒称真实DLL验收。

实际WPF、HID、DPI、双窗、声音/触控、PowerShell、UAC/ACL与恢复旅程继续逐项NOT_RUN，待既有获授权Windows环境；真实SDK/WCS、实体、数量、逐SKU少货及身份批准按能力BLOCKED，Production缺必需能力保持NotReady。


PR24混合消费者读者：Parcel Factory/CLI与Supply Product管理入口统一调用装配分派，再由各业务校验其原格式。Parcel完整校验Effective、Pending和冻结原件，不接受未来schema或悬挂包/模块/reader；Supply原读者仍拥有SQL5及原请求清偿语义。已知无引用混合根允许卸载；缺件/未知/歧义根和任一未决责任继续拒绝。Parcel随装配携带Supply只读维护DLL，Supply随Product携带Parcel共用读者；没有启动第二业务实例或扩展SDK。公共契约仍1.6.0；本轮精确宿主签名示例使用新不可变1.6.8/1.6.9版本，不改变原包或旧请求。
