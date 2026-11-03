# 当前 SM 相机页 MVS SDK 文案收口

2026-10-29，基准 main `924a77ca`。最新 main 已将新相机及当前参数页固定为 MVS 软件读码器，但相机页面仍有“旧版 TCP 收码连接”“无需配置 TCP 端口”等文案，框架顶部仍把双相机 TCP 标成当前入口，造成两种连接方式并存的误解。

相机页改为只说明 MVS SDK 采图、物理身份及软件识别；不适用原设置显示需要按 MVS SDK 重新配置，原按钮仍只在明确点击后重置本相机。删除此页面中无法到达的 SM TCP 参数/实体说明分支。共享 TCP 驱动及历史证据保留原适用范围。单一相机编码、连续采集、逐次交 RCS、完整候选和原请求守卫均沿现有实现。

## 本机验证

正常构建通过，0 警告、0 错误。新工作树第一次 `--no-restore` 构建因缺资产文件而失败，随后使用正常构建还原依赖；不删断言或降低检查。

原生 `--hikrobot-sdk-editing` 退出码 0：首配、日常左右相机均无 TCP 文本/字段，固定只读 MVS SDK 驱动名；不适用原设置不呈现 TCP 方式，明确重新配置后保留另一相机与共用编码；紧凑首配继续使用同一完整候选。[原生检查日志](evidence/sm-mvs-only-ui-01/WINDOWS-WPF.log)、[日常相机截图](evidence/sm-mvs-only-ui-01/bundled-sdk-daily-left.png)、[紧凑首配截图](evidence/sm-mvs-only-ui-01/bundled-sdk-first-compact.png)为本轮合成配置证据，Windows DPI 为 125%。已实际查看截图，驱动名称与帮助可读，紧凑正文按原机制滚动。未执行新安装介质或真实设备测试。

复现：先执行 `. ./scripts/project-environment.ps1`，然后 `dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -v minimal`；构建成功后执行 `dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --hikrobot-sdk-editing`。夹具使用合成配置及实际 WPF，不连接相机或 RCS，不代表安装包已更新、真实 SDK 或现场验收通过。
