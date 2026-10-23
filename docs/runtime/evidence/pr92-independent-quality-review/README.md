# PR92独立复核证据

结论、原始基线、源码符号、六项边界和未验范围见[复核报告](../../../reviews/pr92-independent-quality-review.md)。本目录只保存本机合成/loopback的命令回执、原日志、只读复核脚本和CI状态/选摘；不包含凭据、业务库、图片、厂商SDK或生产数据。

`RESULTS.json`列全部实际命令与退出码，`SHA256SUMS`固定文件原字节；不覆盖原整改目录。`commands`和`logs`分离，早期/最终构建与测试分别保留。最终测试回执记录运行前BaseCommit和工作树测试文件SHA256；CI独立检出的提交身份另存，不能将预提交本地运行改标为最终commit运行。原四个ZIP仍来自7024f1e，未重标新SourceCommit。

本机重现（Windows已有合法日常运行账户/机级配置，全部数据D盘；不为测试改变机级政策）：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --architecture-quality-remediation
```

新增CI的Linux作业使用同一真实Runtime/SQLite/loopback和测试入口，不是Windows UI或现场验收。子进程退出77为夹具明确要求的崩溃，父进程断言核对退出码、真实持久状态及重启行为；父测试最终退出0才算通过。模拟原raw缺失只移除隔离合成根下的夹具文件，未改业务实现。

只读脚本verify-evidence.py与verify-media-readonly.py需要本机保留的原整改证据/ZIP；没有原介质时不可伪造或重新打包冒充复核。三条combination记录重验是读取旧实际运行材料，不是本轮新运行三种发布流程。完整临时运行库及子进程材料保留于D盘日志首行EvidenceRoot，仅供本地复核，不上传。
