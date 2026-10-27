> 2026-10-27 当前策略由[当前合同收口说明](current-contract-cleanup-01.md)替代：仅支持RCS 1.3.5，统一默认5000毫秒（100～30000可配置），旧绑定与数量参数明确拒绝。下文1.3.4及旧绑定预算描述保留为PR116的历史实现/验收身份，不是当前运行规则。

# RCS 请求超时与异常提示

组件 `sportmaster-rcs` / `sportmaster.rcs-task` 新版本为 `1.3.4`。在参数设置的组件配置中选择对应RCS版本，设置“取格口请求超时”，单位毫秒，默认5000，允许100～30000。保存并受控重启后作用于新请求；原请求保留冻结配置、目标和报文。1.3.4省略该字段时采用5000毫秒；旧绑定省略该字段时仅沿用原调用预算，适配器不叠加隐含的5秒截止。变更版本时仍审阅完整候选。

超时展示“RCS请求超时，请重新扫描。”；需要拆包展示明确提示及明细；其他拒绝直接展示原message，缺失时用code。缺失/空白格口不显示成功，已匹配响应的message仍可展示，未匹配响应不借用其中message。工作区不显示旧格口，也不把拆包chutes当作成功分配。

用户已确认允许超时后立即重扫。明确超时后本位允许新的扫码，即使条码相同也创建独立准入和新请求编号；原超时请求保持Unknown和一次发送，历史保留两笔，审计关联前一超时作业。迟到响应不覆盖新扫码结果。原记录仍计入未清偿容量且不参与已结自动删除。只有超时解除此项准入阻断，缺失/空白格口、非法JSON、未匹配响应与拆包不会借此放行。其他健康、身份、暂停及容量守卫保持。重启沿用普通启动暂停，显式恢复后可继续扫码，原请求不会重发。

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

最终重扫增量：超时/异常专项46条PASS日志（含结束标记）、TCP扫码7条、Unknown重启3条、跨业务延后配置58条、时间/存储17条，全部正常结束；核心与Sportmaster WPF Release构建均零警告零错误。原版本兼容机器策略限制仍保留，未更改其产品门禁。后续重扫闭环验证见[增量原件与哈希](evidence/rcs-timeout-errors-01/rescan/FILES.json)。初轮日志保留原字节和当时范围，不扩张为本次放行策略的验证。增量验证通过真实TCP模拟相机连续输入相同条码，覆盖双位并发、超时后立即准入、新编号、原件不变、审计关联、迟到响应隔离、历史及重启后继续扫码。

合并前复核增量（2026-10-26）：评审发现旧绑定在适配器中被叠加隐含5秒截止。新增loopback夹具延迟5500毫秒返回原分配，先在修复前复现TimeoutException；修复后1.3.2、1.3.3省略该字段时均在原10秒调用预算内接受回执且仅发送一次。1.3.4的默认值、显式截止及调用方取消语义保持。超时/异常专项现为48条PASS，心跳27条、TCP扫码7条、Unknown重启3条、生命周期应用层213条，Python兼容声明17项和打包/来源12项通过；核心、桌面及原生夹具Release构建均零警告零错误。

Windows任务链夹具按本包已批准的原message展示核对“合成格口关闭”，同时保留原code和另一投递位结果。生命周期夹具复验定位到候选保存后界面尚未读到Pending，直接调用激活被既有守卫拒绝（stopCalls=0）；现先等待界面读到候选B且入口可用，再等待异步关闭实际结束，保留停止失败不开第二实例、唯一重开及旧请求冻结配置断言，不改产品门禁。修订后原生生命周期18项连续两次通过；完整原生任务层级检查通过。原始失败、成功日志、源码哈希及范围见[复核证据](evidence/rcs-timeout-errors-01/review-fix/FILES.json)。这些是合成身份/端口下的真实WPF回归，不能替代真实RCS/相机、现场验收或安装测试；原版本兼容机器策略限制仍保留。
