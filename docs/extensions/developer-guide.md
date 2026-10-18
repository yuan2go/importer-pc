# 受控扩展：三Application与共享包工具

新Application使用Ipc.Assembly、ParcelConfiguration和StageConfiguration；Supply的StationConfig/SaveConfig/ActivateConfig操作在[Supply扩展指南](supply-guide.md)。包格式、签名、精确公共DLL、不可变ID/version与Store机制共用。Driver拥有读码/称重/采图/PLC端口，Component拥有认证/WCS/上传端口；Application决定主流程，UI只调用契约。

当前Application.Contracts ABI1.6.0，能力示例新包1.7.0（精确目标Release检查后分发），包含PR23的IReaderImageCapture.Retain/Release。观察、作业、活动采集各自保有原Acquisition；拒收撤销观察、消费finally释放观察、安全持久终态释放作业，Unknown/Conflict/原回执责任仍保有。不得取最新帧替代原件。包版本与ABI独立，PR24的Debug1.6.6/Release1.6.7属于其精确宿主；新公共DLL字节须新不可变包，不覆盖历史身份。

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

参数沿现有有限schema：string/integer/boolean/choice/string-list/secret-reference，分组、标签、单位、范围/长度/集合上限、默认、条件必填、只读、说明；EditRole=Maintenance，ApplyLevel=Restart。敏感参数仅存`credential:名称`引用，不能在参数或包中写密码。宿主直接从schema生成表单，不执行包内XAML/脚本。完整可复制声明见[观察示例](../../examples/BarcodeExtension/extension.build.json)、[Parcel能力示例](../../examples/ParcelCapabilitiesExtension/extension.build.json)，项目只引用冻结公共契约；不加入宿主ProjectReference。

## 构建、静态检查与签名

先取得本次冻结宿主（Parcel使用`Ipc.Assembly.dll`，Supply使用`Ipc.Product.dll`静态入口），使用目标宿主相邻的`Ipc.Platform.dll`；Parcel类型化能力还需相邻`Ipc.Application.Contracts.dll`，在PublicContracts声明；Supply兼容集成才使用Ipc.Supply.Contracts。独立私钥放构建者私有目录，信任政策从独立渠道取得，SchemaVersion=1，Publishers为Id/PublicKeyPem/TestOnly；包内公钥不会自动入信。Simulation接受明确TestOnly信任，Production拒绝测试根；通过静态检查不授予生产能力。

```sh
python3 scripts/build-extension.py \
  --definition /independent/reader/packaging/extension.build.json \
  --contract /frozen/app/Ipc.Platform.dll --check-host /frozen/app/Ipc.Assembly.dll \
  --output /new-packages/example.reader-2.0.0.ipcx \
  --version 2.0.0 --publisher approved-publisher \
  --key /private/build/publisher.pem --trust /independent/trust.json \
  --dotnet /pinned/dotnet
```

有包依赖时，为完整闭包重复指定`--dependency-package /original/dependency.ipcx`，顺序不限。`--kind observation|integration`保留兼容快捷入口，只选择对应示例的声明，不再内嵌两套模块/schema/打包规则；不传kind和definition时选观察示例。Parcel能力使用上方模板版本或显式新--version；所选媒介的实际版本和精确身份读取composition.lock.json及identity，旧Supply快捷示例不能替代Parcel类型化能力。

构建器记录声明/源码字节hash与最后实际输入提交；从隔离源码确定性编译，绑定精确公共DLL指纹，签名后调用同冻结宿主`extension-check`。该入口复用`ExtensionPackages.Inspect/ExtractVerified`验证签名、配额、实际托管元数据/入口、依赖闭包和schema，仅在临时目录校验依赖，不注册/安装包、不修改信任、不启动业务或加载插件。多余、缺少、冲突、循环、变更的依赖拒绝。宿主全文件前后hash与插件输入再次核验后，才独占发布完整`.ipcx`及`.identity.json`。失败/取消不产生成功包；原输出存在就拒绝覆盖。RSA-PSS签名随机；可复现性比较原始manifest和payload，不要求ZIP签名字节一致。

可单独执行同一静态验收（dependency-files是签名依赖包绝对路径JSON数组）：

```sh
dotnet /frozen/app/Ipc.Assembly.dll --action extension-check --mode Simulation \
  --file /new-packages/example.reader-2.0.0.ipcx --extension-trust /independent/trust.json \
  --dependency-files /independent/dependencies.json
```

此检查不替代安装时永久ID/version登记、机器策略、ACL或实例启动。macOS交叉打包win-x64自包含候选时，不能执行Windows自包含宿主。为构建器提供`--check-host /frozen-portable/app/Ipc.Assembly.dll`，或为package-application.py提供`--extension-check-host`；旧Supply另按自己的发布入口。检查器必须来自冻结可运行宿主，Ipc.Platform与全部声明公共契约逐字节匹配目标，且两份宿主前后不变；任何差异拒绝，不放宽目标合同指纹。旧冻结宿主没有新静态入口时用其原版构建器/原包，不混用不同公共DLL冒称兼容。公共DLL指纹变化即便契约主版本不变也要新不可变包版本；T1原请求固定原实现、合同及目标，缺原能力精确阻塞，不能通过换包/换ID重发解除。

## Parcel安装、绑定与维护

目标包使用实际Release宿主的Platform和Application.Contracts；参考声明见[Parcel能力示例](../../examples/ParcelCapabilitiesExtension/extension.build.json)。使用`--definition`；声明的PublicContracts从目标Platform旁读取Application.Contracts，`--check-host`须逐字节匹配目标公共DLL。私钥外置，TestOnly只用于Simulation；真实SDK/协议/资格仍BLOCKED。

```sh
dotnet /frozen/app/Ipc.Assembly.dll --action extension-inspect --mode Simulation --file /media/selected.ipcx --extension-root /reference/store --extension-trust /independent/trust.json
dotnet /frozen/app/Ipc.Assembly.dll --action extension-install --mode Simulation --file /media/selected.ipcx --sha256 ORIGINAL_UPPERCASE_SHA256 --extension-root /reference/store --extension-trust /independent/trust.json
```

安装成功后，在完整schema3候选中加入精确Extensions.Packages、Instances；reader.Driver=`extension:实例ID`。类型化能力Implementation=`extension:包ID/模块ID`、Package/Module与库存保持同一修订；每个位置绑定自己的稳定设备。校验后用加载时配置Revision CAS保存PendingRestart，清偿旧责任后受控退出/重启，资源成功才确认Effective。安装、绑定、激活、健康、可接纳是独立状态。完整命令/恢复/安全卸载及离线备份消费者见[维护指南](../runtime/architecture-quality-closure-01.md)。

新Application修改清单：所属Flow与Desktop、Assembly/Desktop的条件ProjectReference和注册根、compositions产品闭包/视图、package-application及对应锁文件；超出Parcel语义可独立IBusinessRuntime，不修改Platform为万能业务引擎。现有公共ID、配置键、原JSON和包身份保持兼容。Windows安装/UAC/ACL、实际SDK资源寿命及现场逐项NOT_RUN/BLOCKED。
