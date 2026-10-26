# RCS 请求超时与异常提示

组件 `sportmaster-rcs` / `sportmaster.rcs-task` 新版本为 `1.3.4`。在参数设置的组件配置中选择对应RCS版本，设置“取格口请求超时”，单位毫秒，默认5000，允许100～30000。保存并受控重启后作用于新请求；原请求保留冻结配置、目标和报文。旧绑定版本及省略配置的行为保留，变更版本时仍审阅完整候选。

超时展示“RCS请求超时，请重新扫描。”；需要拆包展示明确提示及明细；其他拒绝直接展示原message，缺失时用code。缺失/空白格口不显示成功，已匹配响应的message仍可展示，未匹配响应不借用其中message。工作区不显示旧格口，也不把拆包chutes当作成功分配。

尚待选择超时后的重扫放行策略。现有Unknown阻断保持，提示不会释放本位或自动重发；尚不能描述为立即重扫闭环。拆包沿原检查处置流程。

源码入口：集成组件的RcsTaskOptions、SportmasterRcsTaskService、RcsDestinationProtocol及parameters.json；业务SportmasterFlow.RequestTimeoutMs；执行层预算和原件保管；共享桌面及供包台位投影。未新增公共线协议/数据库字段，旧回执序列化身份保持。

本机复现（先执行环境脚本，全部运行数据位于D盘）：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --rcs-timeout-errors
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --rcs-heartbeat
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --p0-rcs
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --p0-unknown-restart
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --version-compatibility
python -m unittest discover -s scripts -p test_application_compatibility.py -v
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true
```

验证使用独立Simulation组合及loopback HTTP，生产身份检查不变。初轮新增夹具使用生产组合触发机器身份/资源租约门禁，已改用既有P0SimulationFactory。普通快照按设计隐藏HTTP原件，原件断言改读实际持久记录，未删除断言或放宽产品门禁。测试覆盖默认值/边界、独立超时、调用方取消、响应读取卡住、总预算、无自动重发、正常/拆包/message、缺少/空白格口、非法及未匹配回包。

最终本机结果：[原始日志及SHA256清单](evidence/rcs-timeout-errors-01/FILES.json)。新增超时/异常专项30条PASS日志（含结束标记）、心跳27条、真实TCP合成扫码7条、Unknown重启3条、时间/存储17条均正常结束；兼容声明Python测试17项通过；Sportmaster WPF Release构建零警告零错误。既有version-compatibility执行至19条PASS后因未安装Windows机器运行策略而停止，未将其标记为整套通过，也未修改生产门禁。真实RCS、相机、现场联调、安装/升级及原生窗口视觉验收未执行；模拟、构建和CI不能替代这些验收。
