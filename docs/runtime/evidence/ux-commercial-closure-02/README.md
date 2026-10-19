# UX闭环02证据索引

[逐包结果](../../ux-commercial-closure-02-results.md) · [产品/产物](ARTIFACTS.json) · [源字节](source-parity.json) · [原/公开字节](log-custody.json) · [资料差额](archive-delta.json)。产品固定f1a655c7d14e4ee012ea17694533f996a03c9246；代码实际测试源文件与冻结源码对应，后续为证据/状态/指南勘误。Windows原生NOT_RUN、真实SDK/合同/现场BLOCKED。

- selection-red.log保留修前A→B失败；presentation-final.log覆盖两个VM慢快照/慢历史/原件打开/清选择/实际命令Owner、Busy/重复互斥/独立Pause/关闭所有权及UI上下文。
- checks-dev.json为开发时9组原执行；ux-commercial-configuration.log保留初次失败，configuration-cas-green.log为精确前置守卫重试后相同CAS断言通过。其余同名日志按入口读取。不是把首轮exit-6改成0。
- core-build-final.log、wpf三业务及wpf-verifier-build-delivery.log是必要构建。原生验证器真实控件/客户区代码已交叉编译；没有Windows执行日志或截图。
- publish-checks.json、media-verification.json为实际5个产品ZIP，unpacked-smoke-checks.json及三个*-smoke.json是实际解包启动/原件重启；selected-journey.json覆盖签名包安装/配置/受控重启/启动失败恢复/安全退休，rcs-journey.json覆盖真实loopbackHTTP原报文/回执及不重POST。
- raw-verification-evidence.zip在参考Release，保留本次原始日志/JSON/事件字节。当前可读JSON/日志只替换本地路径标签和显式标记尾空格，具体RawSha256/PublishedSha256见log-custody.json；重验原事件hash用原ZIP，不对路径替换后的副本假称原字节。原环境路径是开发定位，无SDK/客户数据/生产凭据/私钥。
- reference-trust.json只允许本次Simulation/TestOnly参考发布者，不可用于Production。selected-release-journey.py继承PR37合成旅程，仅适配新版本身份；须使用新隔离输出目录和对应5ZIP中的Sportmaster核心/选装portable，保持原请求/hash/守卫断言。
- VERSIONS-ERRATA.md独立纠正候选指南末段一处PR37版本残留；原ZIP及其hash不覆盖。当前生产/配置/构建输入与固定产品一致，唯一当前随包指南勘误差额在source-parity.json。

重跑入口：构建tests/Ipc.Application.Verification，再用dotnet <verification.dll> <source-root>加--ux-commercial-presentation、--ux-commercial-inputs、--ux-commercial-configuration、--audit03-snapshots、--sportmaster-ui、--sportmaster-window-lifecycle、--version-compatibility、--resource-risks或--parcel-recovery-p2。回放历史证据需使用其明确源码及新隔离根，签名包须匹配精确公共DLL。
