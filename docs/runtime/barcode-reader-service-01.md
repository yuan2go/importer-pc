# 独立读码服务 · 当前实施与运行边界

[设计](../design/barcode-reader-service-01.md)定义内部行为，[协议 v1](../protocols/barcode-producer-v1.md)定义线上合同。当前为未完成的核心实现，不能部署为读码服务或启用供包台业务。

## 已实现

- `src/Ipc.Drivers.Hikrobot.Mvs/MvsCaptureSession.cs`：唯一 MVS native owner；原诊断包装仍 observe-only。服务可经 `BundledMvs.CreateCaptureSession` 注入有界复制入口及已裁决的所有权策略，工厂要求明确物理身份和触发核对。
- `MvsFrameSink.cs`：借用期复制、Free 后提交/Free 失败拒绝的两阶段端口。原 SDK 清理失败保有、取消等待不释放的规则不变。新增设备时间戳按同版 CameraParams.h 的12/16字节字段读取；未改变ABI或厂商字节。
- `services/BarcodeReader/Ipc.Reader.Core`：只读解码端口、固定 POH 池、队列及停止所有权、触发归并、版本化选择、就绪门控、离线回放库。无 VM/MVS 程序集依赖，独立锁文件；不在主解决方案加入 .NET Framework。
- `Ipc.Reader.Verification`：可编程假解码器和23项风险检查。就绪测试验证门控输入失效会关闭，不能证明真实授权状态可读。

复用既有解码框架的端口、超时保有、选择器和回放逻辑后适配到独立核心，不在供包台 MVS 扩展启用解码。源码和复用基线在PR中登记；旧回执保留原SourceCommit。

## 必需信息与未完成

1. 现行v1由供包台持相机租约，服务只取SDK独占；本轮服务持同键的要求互斥。尚待决定遵循v1，还是授权同时调整双方协议和租约。心跳也只能按现有精确字段发送，不能私加就绪字段。
2. 未找到VM实际安装登记/Development方案SDK。本机找到官方基础安装包及解压的算子SDK；安装指南和脚本项目表明 .NET Framework 4.6.1及以上，但不能据此猜测 `.sol` 方案API、依赖和授权接口。需实际安装目录或对应方案SDK指南/示例。
3. 服务宿主、VM适配/worker、方案节点接口、版本化完整配置/哈希、TCP服务端、监督/开机启动、回放CLI和黄金/端到端验证未完成。当前只有设计及可验证的独立核心；没有空成功适配器或模拟生产回退。

## 复现核心验证

在仓库根目录执行；数据和缓存由项目脚本保持D盘：

```powershell
. ./scripts/project-environment.ps1
dotnet build services/BarcodeReader/Ipc.Reader.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet services/BarcodeReader/Ipc.Reader.Verification/bin/Release/net10.0/Ipc.Reader.Verification.dll
dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false
```

原Probe自测仍按项目冻结Platform路径构建后使用 `--self-test`，不执行真机连接。当前结果及原始失败见[本包回执](evidence/barcode-reader-service-01/README.md)。

真实VM加载、授权/加密狗失效、方案、图像识别与错读率、触发语义、真机复制性能、24小时稳定性、Windows服务监督和实际供包台端到端均未验。未执行安装器、设备控制、生产资格或签名。
