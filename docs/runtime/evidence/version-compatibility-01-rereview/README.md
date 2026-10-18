# PR29 P1/P2整改证据

修复运行/发布闭包 **5e3282b02616d905a628a6cbeeb9a7fbe4228a79**；独立审查head d954ba6，main baac260。原c4e1319候选和全部既有证据未改。

- [原审查失败原件保有](original-review-custody.json)：仅公开SHA回执，原审查报告、改标记录、原事件和维护探针原字节本地保有。
- [Python真实red](python-red.log)、[C#真实red](csharp-red.log)与相邻build日志保持原始输出；未删断言。Python原始red输出含行尾空白，保留原字节，不称全仓diff --check通过。
- [Python12项green](python-green.log)、[全部38项Python](python-all-final.log)、[三业务版本/维护入口green](csharp-green.log)和三个*-wpf-locked-build.log。
- [历史五条记录重验及原改标拒绝](historical-record-reverification.json)、[原CLI改标拒绝](p1-original-probe-rejected.log)。旧schema1没有独立输入事件，此次不补写历史输入或修改旧记录。
- [新候选](candidates.json)与[三条新schema2运行](combination-index.json)：默认、BeforeMain、Attached真实进程旅程及重验PASS；配置自洽改标被拒绝。公开记录仅Command本机路径投影，原件SHA另列；事件原字节不变。
- [新portable实际发行DLL共享入口](published-maintenance-portable.json)动态通过；[Windows实际DLL静态IL](maintenance-il.json)证明app/desktop的安装、恢复/卸载都调用共享入口，并从实际businessAssembly字段传入四参校验器。探针源为maintenance-il-probe.cs.txt和maintenance-il-probe.csproj.txt，可在独立目录改为Program.cs/Probe.csproj，用dotnet build后传入实际Ipc.Assembly.dll验证。
- [Windows DLL加载阻塞摘要](windows-load-attempt.json)及[实际异常输出](windows-load-confirmation.log)：Darwin arm64不能加载win-x64宿主。没有执行Windows/UAC/ACL/真实设备动作。

Windows实际NOT_RUN；真实SDK/WCS/PLC/认证/实体/现场BLOCKED；新候选未重新签名激活扩展，旧2.0.1签名组合PASS仅对c4e1319原宿主有效。业务逻辑和版本1.2.1、内嵌声明及公共接口未改；构建产生的公共DLL精确SHA可能变化，不能凭相同版本复用旧包资格。

[整改结果](../../version-compatibility-01-rereview-results.md)。自带hash及事件关联证明一致性，不代替受信索引/发布渠道或现场批准；同时替换整份记录及事件须用独立信任锚检测。
