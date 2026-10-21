# PARAM-UX-02 / PS-05 原生交付验证

执行基线为 main `767075abe62a77c30e70766bb5d3f1feca02686b`，开工工作区干净，开放 PR 为 0；PR78/79 已合并。实现提交为 `f4ce7b6db37599eb5b5da7e645423c6e30fa047c`；补齐原生夹具等待、健康检查调度及 CI 后，最终构建源固定为 `ce9dcd32ebc5b9339d76a8c7482594cc2c16b308`，开发分支 `codex/parameter-v3-native-delivery`。新业务版本 1.7.0；旧 1.6.1 未改名复用。后续证据记录提交不改写此介质 SourceCommit。

## 变更及审查

- UI、保存审阅、Application 和权威 Inspect 共同区分 Unchanged；没有差异不产生 Pending，也不推进配置 Revision。保留原权限、原因、RuntimeId、CAS、审计与未知原提交责任。
- Sensitive 与 secret-reference 均用 PasswordBox；首配/日常共模、导航保值与审阅遮罩一致。UI Automation 不返回真实引用。
- 相机与共享 RCS 使用 Effective 快照显示运行地址、组件版本、配置指纹和当前证据，Pending 独立说明，快照失效显示未知。RCS 本地就绪不能解释为远端接受。
- 对象“连接诊断”进入既有路径，保留实例身份；返回同对象缓存表单。没有新 Probe、业务试单、配置账或连接生命周期。
- 相机连接、取格口/心跳编码和识别缓冲分组，表单左对齐；从高级 JSON 采用/重新读取后恢复表单，普通快照刷新不把技术任务切走。
- 实际键鼠发现并修复：错误字段修正后离开输入仍残留旧错误；首次打开参数恰逢 Busy 时读取被略过。现在实际编辑后离开字段重新校验本地字段，首次读取等忙碌结束后执行一次；不会边输入边重建表单、反复自动重试或自行提交。
- 修复 PR78 后定向验证工程遗漏的生产 partial 文件引用；修复原生夹具把一次被合并的 RefreshAsync 当成已呈现新投影的竞态。原断言和业务等待条件保留。

使用项目 UI/UX Pro Max 的独立 `--stack wpf` 查询及 Impeccable Operate / WPF Assessment A。按 V3 高清图、原型源码、HANDOFF/字段迁移复核；本环境浏览器拒绝 file:// 原型入口，未将静态读取计为新原型交互 PASS。原设计包 54 个文件及原哈希不改写。

Operate 结果：重点以对象标题、运行上下文、字段分组、统一保存依次呈现；去除表单横向居中的空隙；保持四类单目录、输入字号/行距、原生焦点框、独立目录与内容滚动。实际走查覆盖错误摘要、保存条件、跨对象草稿、失效快照、对象诊断和紧凑客户区。未使用网页/CDP作为 WPF 验收。

## 验证层次

| 层次 | 实际结果与证据 |
|---|---|
| 定向保存/恢复核心 | [39项 PASS](../evidence/parameter-settings-v3-native-01/core.log)：Unchanged、CAS、Unknown 原提交核对、冻结候选、原始无效输入与保留责任。生产代码为 ce9dcd3；收口提交仅补非 WPF 测试替身的 BeginInvoke 调度接口，不把它算作原生 Dispatcher 证明。 |
| 冻结发布 DLL 的 WPF | [RESULT](../evidence/parameter-settings-v3-native-01/native/RESULT.json) PASS，SourceCommit 与 FixtureSourceCommit 均为 ce9dcd3，FixtureMatchesMedium=true；[142项断言](../evidence/parameter-settings-v3-native-01/native/WINDOWS-WPF.log)覆盖原任务闭环、首配五步、保存审阅、留稿、敏感字段、有效运行摘要、失效快照与对象诊断。 |
| 尺寸与缩放 | 原生客户区 1366×768、1092×614、800×420 DIP，实际 Windows 缩放 125%；底部动作与滚动可达。尺寸经原生像素取整，精确值见日志。1092×614 DIP 约对应 1365×768 物理客户区，不宣称在独立1366×768显示器或100%缩放通过。 |
| 实际键鼠 | [搜索及中文草稿](../evidence/parameter-settings-v3-native-01/manual/search-retains-chinese-draft.png)、[无效原文定位](../evidence/parameter-settings-v3-native-01/manual/invalid-input-located.png)、[修正后清除错误](../evidence/parameter-settings-v3-native-01/manual/corrected-error-cleared.png)、[返回保留草稿](../evidence/parameter-settings-v3-native-01/manual/returned-draft.png)、[对象诊断](../evidence/parameter-settings-v3-native-01/manual/object-diagnostics.png)及返回。Ctrl+F、Enter、Ctrl+A、Tab、鼠标导航均在最终冻结介质的真实窗口执行。中文文本输入不等于完整中文IME组合键测试。 |
| 共享桌面消费者 | [DirectPlc](../evidence/parameter-settings-v3-native-01/directplc-build.log)与[WcsToPlc](../evidence/parameter-settings-v3-native-01/wcstoplc-build.log) Release 构建均0警告/0错误。版本兼容测试17项 PASS；发布指南测试2项 PASS。 |
| 安装器启动冒烟 | 最终EXE实际解包、核对完整性并显示[1.7.0安装选项](../evidence/parameter-settings-v3-native-01/startup/installer-options.png)，可编辑隔离路径并取消。未点击安装，未写机器登记、授权或快捷方式。 |
| 发布EXE启动冒烟 | 显式 --maintenance 加隔离目录，分别读取[缺失配置](../evidence/parameter-settings-v3-native-01/startup/empty-config-maintenance.png)和[既有合成配置](../evidence/parameter-settings-v3-native-01/startup/existing-config-maintenance.png)。扩展库存缺失如实显示，保持离线、设备未启动。正常安装后的首次启动生命周期不由这两项替代。 |

设备、组件、授权会话与RCS上下文均为显式合成夹具，未连接真实设备。相机/RCS参数截图使用公开schema与合成值；手工走查的运行窗口使用manual模拟能力，所以显示“此版本没有可配置参数”是该实际能力的结果。

实际发现及修复保留：最初验证工程缺 partial 引用导致编译失败；最初 PasswordBox 断言误以为无 Value Pattern，已按原生实现检查 IsPassword 与空 Value；原生恢复 JSON 后编辑器隐藏为实际产品缺陷，已修复；轮询投影及快照失效后的保存夹具竞态已改为等待实际 Fresh、CanConfigure、Pending 状态；截图使用明确本地 Viewbox 防止不可见后代扩大自动取景范围。没有删除关键断言、吞异常、把重试次数当成功或改写原失败文件。首次 f4ce7b6 冻结介质在失效恢复后断言 Pending 时报 NullReferenceException，[原 FAIL](../evidence/parameter-settings-v3-native-01/f4ce7b6-failed/RESULT.json)与日志保留。保存夹具使用已有 BeforeMonitorProbe 测试调度接缝控制周期检查并排空在途检查，生产 CanConfigure 守卫不变；原始拒绝证明没有绕过守卫。中间9c801a1走查还发现旧错误未清除，[原截图身份](../evidence/parameter-settings-v3-native-01/manual-before-field-error-fix/README.md)保留。f4ce7b6、b9c56ce、6994d94、9c801a1介质均被最终ce9dcd3替代，不能用于本次最终交付声明。

中间构建遭遇系统盘空间不足，切换本任务TEMP/TMP到D盘后成功；旧失败日志保留在本机。清理C盘本任务旧临时目录被自动审批策略拒绝，未绕过。最终构建和测试使用D盘隔离临时根。归档检查以Git对象字节为准，避免Windows换行转换造成伪哈希差异；最终检查仍有与基线相同的298项历史问题，新增0项，不改校验器、豁免或历史资产来制造PASS。

## 最终交付身份

[构建身份](../evidence/parameter-settings-v3-native-01/BUILD-INFO.json)、[介质回执](../evidence/parameter-settings-v3-native-01/DELIVERY.json)和[环境/边界](../evidence/parameter-settings-v3-native-01/ENVIRONMENT.json)与最终截图同属 SourceCommit `ce9dcd32ebc5b9339d76a8c7482594cc2c16b308`。

| 产物 | SHA-256 |
|---|---|
| CainiaoSupplyStation_SM_Setup_v1.7.0_win-x64.exe，177713397字节，未签名 | `6706ab52f7fbbed0ead9a60b67064a8ddf86cd66098f7c57def2315d58a3d0d7` |
| sportmaster-win-x64-ce9dcd32ebc5.zip，106427922字节 | `c06881f975c9371941c028080b903b1e407567c1da20f5d6353c44cd313fecd6` |
| 介质SHA256SUMS | `fa05160d53b93b98b24374a929d8922f6f7a17889f3d88ef256ae58d89792cf2` |

交付本机路径为 `.artifacts/parameter-v3-delivery-installer/` 与 `.artifacts/parameter-v3-delivery/`，构建脚本核对安装器嵌入介质及共享DLL字节。EXE/ZIP未作为源码上传Git；PR保留公开源码、合成截图、回执及运行说明。证据收口提交不改写包内SourceCommit，也不把后续PR合并树自动声明为已验证介质。

## 运行与边界

操作和复现入口见[1.7.0 原生运行说明](../parameter-settings-v3-native-01.md)。参数原型、原生 WPF、冻结安装介质、真实 SDK/RCS 与现场分别记账。现场实体/权限/合同未提供，Production 资格不在本轮声明范围。Windows 100%物理缩放、触摸、目标 IPC、管理员/第二账号/重启/升级回退完整生命周期需要授权测试环境；未执行的项目明确 NOT_RUN。
