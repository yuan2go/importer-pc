# 恢复P2证据入口

固定源码与结论见[结果](../../parcel-recovery-p2.md)，计划及文档先行提交为04ac845/f0abce6，业务源码fe4a4a2。

- `original-failures/identity.json`逐项列原FAIL、独立PASS、Wait-only PASS及受控旧DLL探针的原字节hash。失败不替换为新PASS。
- `red.log`为本次恢复补码前6项失败；`build-green.log`记录局部变量编译失败；`p2-green-first.log`为失败构建后误用旧输出目录再次得到6项FAIL。`build-final.log`及`build-frozen.log`为之后完整构建。它们不会被汇总为通过。
- `p2-final.log`、`custody-controlled.log`、`custody-concurrent.log`及配置、维护、旧发布、架构、签名扩展日志为实际运行输出；`raw-output-sha256.json`逐字节列hash。日志含合成夹具的原本机路径，未改写原文。
- `genuine-delivery.original.log`与`selected-journey.original.log`是本地原JSON输出的同字节日志副本。相应`.json`明确标记为仅路径公开投影，附原SHA；投影不能用于冻结请求或回放。
- `archive-checks.json`为FAIL267，`archive-comparison.json`记录与既有错误完全相同、新增0，没有新增例外。
- `release-identity.json`记录ZIP、签名包和宿主公共DLL；ZIP逐文件SHA验证，SQL1～5原文件与main逐字节比较。私钥、独立信任、数据库、图像与二进制未入库。Windows交叉构建不代表实机运行。

复现必要定向入口（新隔离数据目录；不要向生产目标运行）：

```sh
/pinned/dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false --artifacts-path /new/build
/pinned/dotnet /new/build/bin/Ipc.Application.Verification/release/Ipc.Application.Verification.dll . --parcel-recovery-p2
/pinned/dotnet /new/build/bin/Ipc.Application.Verification/release/Ipc.Application.Verification.dll . --receipt-custody
```

签名组需按实际宿主PathMap及源码构建字节一致的公共DLL，之后按扩展开发指南显式新版本、独立信任运行`--quality-extensions`。真实旧发布组须提供完整匹配原发布和实际旧夹具；没有原件就标NOT_RUN，不能改新库版本伪装历史。
