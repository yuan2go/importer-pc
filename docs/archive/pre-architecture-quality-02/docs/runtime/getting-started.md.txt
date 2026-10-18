# 首次运行与首条参考作业

当前工程入口对应[UI-MAINTAINABILITY-16](../work-packages/WP-IPC-UI-MAINTAINABILITY-16.md)，无需沿历次工作包寻找启动命令。[运行/部署/恢复](operations.md)、[独立扩展开发](../extensions/developer-guide.md)分别提供正式维护路径。这里仅使用获授权合成数据、本机SIMULATION-v1接收端；不控制真实设备，不宣称SDK/WCS或现场通过。

## 从源码首次启动

构建机需要`global.json`锁定的.NET SDK10.0.402、Python3、Git和OpenSSL（签名包时）。macOS/Linux只运行核心；WPF在Windows运行。首次还原需取得NuGet锁中的依赖，无网络/无包缓存时保持明确失败，不改锁绕过。

```sh
dotnet restore ImporterPc.slnx --locked-mode -p:EnableWindowsTargeting=true
dotnet build ImporterPc.slnx --no-restore -m:1 -nr:false -p:EnableWindowsTargeting=true
python3 scripts/seal.py --bin src/Ipc.Product/bin/Debug/net10.0
# Windows WPF启动前也封装其同版输出：
python3 scripts/seal.py --bin src/Ipc.Desktop/bin/Debug/net10.0-windows
```

seal写实际模块SHA/配置/发布身份；二进制变更后重做，不能跳过哈希校验。当前契约1/SIMULATION-v1，Storage/MinimumReader=5/5。旧库需要[显式备份升级](operations.md)，不能把新根/换seed当旧部署迁移。首次合成开发使用全新数据根，避免与其他进程、候选、原数据库共享。

终端一启动合成本地接收端；终端二运行同版核心控制台：

```sh
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /new-reference/receiver
dotnet src/Ipc.Product/bin/Debug/net10.0/Ipc.Product.dll --config src/Ipc.Product/bin/Debug/net10.0/config --data-root /new-reference/client --action serve
```

fixture与`config/station.json`的Integration.Endpoint必须一致。默认参考端点以实际配置为准；改端点使用本地合成配置副本和同版封装，不把示例端口解释为现场地址。Windows已发布客户端先完成指定运行账户、ProgramData机器策略与ACL配置，使用候选自带`station.ps1`，不从可写Downloads目录提升权限。

## 第一条参考作业

在控制台每行提交一条输入。先`snapshot`观察服务Ready/来源确认及阻断；真实未就绪时按诊断处理。命令中的ExpectedRevision必须替换为刚读取的State.Revision，Id用本次唯一ID。显式Conflict表示未执行，可重读；异常/未知结果先查询原ID，不能盲重发。

```json
{"Action":"command","Command":{"Id":"first-login","ExpectedRevision":0,"Kind":"Login","Value":"maintenance"}}
```

登录后再次snapshot取新revision，以`OpenContext`和Value=`000001`开合成箱；等待Session.Status=Active后提交来源观察：

```json
{"Action":"emit","Lane":"left","Kind":"Enter"}
{"Action":"emit","Lane":"left","Kind":"Read","Codes":["000123"],"Quantity":2}
```

等待左通道AwaitingAction，从`State.Lanes.left.Id`取原作业ID、从当前snapshot取revision，发送`Submit`命令，OwnerId用该原作业ID。等待Accepted，AcceptedQuantity应由唯一接受账给出2；识别数量、作业量、请求接受事实分开。其他阻断或不同状态时读取原请求/报警，不修改状态或自动Resume通过。离位/关闭继续按当前应用守卫和原物理/业务事实操作。结束输入`quit`受控停止；终端一结束fixture。

Windows桌面用主屏箱码、双通道来源、固定提交及关闭动作执行同一旅程；每窗草稿、筛选和滚动独立，业务实例/连接/发送器唯一。软件暂停不是安全急停。实际HID/触控、焦点、DPI、双窗、声音、UAC/ACL与恢复仍须同版Windows逐项验收，不能用此控制台路径替代。

## 定向验证和发布

先用`--help`读取真实覆盖。默认reconciliation包含原AC/进程、H04/A01/E07/E08/E11/E12及AC14/15；architecture、maintainability和health-profile是显式独立范围。

```sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --help
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --maintainability
python3 -m unittest discover -s scripts -p 'test_extension_build.py' -v
```

按改动选择必要回归；源码/配置/脚本/指南提交干净后发布全新目录，原输出保留。Python和PowerShell调用同一Python打包权威，支持core-reference、barcode-reference和integration-reference；后两者需外部私钥和独立TestOnly公共信任。完整供包参考发布例：

```sh
python3 scripts/package-reference.py --target portable --composition integration-reference --output /new-candidates/wp16-portable --extension-key /private/build/reference.pem --extension-trust /independent/trust.json
python3 scripts/package-reference.py --target win-x64 --composition integration-reference --output /new-candidates/wp16-win-x64 --extension-check-host /new-candidates/wp16-portable/app/Ipc.Product.dll --extension-key /private/build/reference.pem --extension-trust /independent/trust.json
```

Windows等价入口：`./scripts/publish.ps1 -Composition integration-reference -Output <new> -ExtensionKey <private> -ExtensionTrust <independent>`。core-reference不含后装包；Production不允许参考测试包，缺必需真实能力保持NotReady。非供包/供包装配裁剪使用`package-business.py --composition inspection-reference|supply-assembly`，不会被publish.ps1误映射为完整供包。

解压到新目录并核对SHA256SUMS/组合/Package和SourceCommit后运行：

```sh
python3 scripts/smoke.py --product /new-unpacked/app --fixture /new-unpacked/fixture/Ipc.Fixture.dll --output /new-evidence/core.json
python3 scripts/smoke-extension.py --release /new-unpacked --output /new-evidence/extensions.json
```

portable需要.NET10运行时；win-x64自包含仍需实际Windows。原始manifest/payload、公钥策略、独立插件源提交/哈希与宿主来源分别记录；新参考包1.0.4不覆盖旧1.0.0～1.0.3。候选成功不表示已安装、已激活、Windows通过或现场资格。

## 常见阻断

|现象|实际下一步|
|---|---|
|启动模块hash/配置不符|核同版源码与产物，构建后seal；已分发候选先核原SHA，保留原件，不在线改已签包。|
|Production NotReady|取得真实SDK/WCS合同/身份/实体/数量/逐SKU资料及批准，逐能力接入；不切模拟伪装生产。|
|来源Unconfirmed/过期、WCS离线|核来源/服务实例、目标/epoch/时效和本地fixture；读取snapshot不续期。恢复健康后仍需处理其他独立阻断。|
|Unknown或独立接收原件未取得|查询原请求的冻结实现/合同/目标，保留原ID/报文/hash与Q1/Q2责任；无权威原件/合同保持隔离。|
|SaveConfig拒绝/CAS冲突|按字段定位修草稿，重读当前revision；保留窗内草稿比较Effective。保存后configs取原修订，显式激活并受控重启。|
|Pending/ActivationFailed|读原修订原因，核精确包/schema/依赖/资源；失败保旧，确认完全停止后修复再启动，不手改库。|
|包同ID/version内容冲突|核永久身份与原manifest，恢复原精确字节或用新版本；不删identities/registration。|
|卸载LegacyConsumersUnverified/租约占用|盘点真实离线根/备份及未决原请求，等待实际Stop/查询/T3终止；不删租约绕过。|
|停止超时/清理失败|保留当前进程资源诊断和原操作，不启动第二所有者；原任务终止后重试受控清理。|

完整恢复、备份/升级/回退、新根隔离、Windows部署和解除依赖资料见[运行指南](operations.md)。
