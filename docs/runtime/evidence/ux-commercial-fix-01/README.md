# UX-COMMERCIAL-FIX-01 证据

[实施结果与边界](../../ux-commercial-fix-01-results.md) · [checks](checks.json) · [产物身份](ARTIFACTS.json) · [媒介完整性](media-verification.json) · [三业务解包启动](unpacked-smoke.json) · [选装主链](selected-journey.json) · [实际签名RCS原件](rcs-journey.json) · [发布输入对应](source-parity.json) · [资料校验差额](archive-delta.json)。

产品/媒介固定为c384c3bc4750bbaafbcc3b834d3b12aa126c3b41；14组冻结回归同源。补充签名RCS夹具b38a2d323ff21b7b5a7da798b8d3008fc75730d4只修归档回执处理，生产依赖逐文件取自冻结版本，checks中单列验证器SHA和依赖hash，不能当作产品重构建。

logs保留可公开的原输出，只有本地目录标签替换；checks同时记录原日志SHA256及公开日志SHA256。完整原输出、开发失败、原SQLite/HTTP接收器数据、命令和中间产物留在本地.artifacts/ux-commercial-fix-01；无客户数据、生产设备、SDK或私钥入库。日志中的引用模拟、event/timer替代及同步上下文验证不能证明WPF原生表现。

重跑一般回归：在相应固定源码构建tests/Ipc.Application.Verification，使用`dotnet <verification.dll> <source-root> --ux-commercial-inputs`、`--ux-commercial-presentation`、`--ux-commercial-configuration`等checks中的标志；P2使用明确dotnet DLL宿主。签名包验证必须匹配精确公共DLL，不能用任意新构建替换冻结宿主。public reference-trust.json仅是本次独立Simulation/TestOnly参考信任，不可用作生产部署信任；未收入私钥，不能据此重签并覆盖旧身份。

[完整选装回放脚本](selected-release-journey.py)继承仓库内既有通用脚本并只更新本次版本/前置拒绝回执处理。用新隔离目录放入ARTIFACTS所列sportmaster-core.zip及sportmaster-selected-portable.zip，再执行`python3 selected-release-journey.py <new-media-directory> <dotnet> <reference-trust.json> <frozen-source-root>`。只使用本地参考资源/loopback，保留每次回执和失败；不是Windows/正式协议验收。
