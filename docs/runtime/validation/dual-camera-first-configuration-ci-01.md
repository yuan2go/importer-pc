# PR93 首次配置CI续修

2026-10-24，任务WP-IPC-DUAL-CAMERA-COMMUNICATION-FIX-01。基线main `c2300cdb73bda9192e2e7912b4fee12753b81121`，PR93 HEAD `b6e9350f2664bd2ff3c6f2b52970f9ef24300a00`。执行时PR93已合并，续修使用独立分支 `codex/dual-camera-first-config-ci-fix`。原工作区及4个未跟踪附件不动。开工GitHub无其他开放PR。

## 原失败与根因裁决

[实际CI运行39997147826](https://github.com/yuan2go/importer-pc/actions/runs/39997147826)，`aqa01-boundaries`通过，`frozen-sportmaster`的原生首次配置失败。作业120563521301日志明确冻结SourceCommit为 `bad2734cc49c07fd6077cbcdc590e4817fcff9ec`，两种介质生成完成；`ParameterUiRefinement.cs:23/33`提取 `device.ip` 的Single抛出异常。参数壳和后续冻结旅程未运行，安装器未生成；独立视觉步骤通过不补偿此失败。

本机Windows在未修源码上重新构建原生夹具，使用原HEAD已构建宿主，复现同一调用栈。本地复现属于开发路径，CI日志才是原冻结介质身份依据，二者分开。

已证实的真实功能回归：`TcpLineObservationSource.Descriptor` 与装配资格所选TCP版本均为1.0.1，但 `StartupConfigurationDraft` 及 `LoadStructuredConfiguration` 把内置DeviceBinding转为CapabilityBinding时硬编码1.0.0。CapabilityDraft按版本精确查找返回null，真实页面参数集合为空。`FillInitial`仅遍历现有字段，不创建字段；所以不会修复缺失目录。验证中的Single准确暴露缺陷，应保留。不能把manual或海康的不同字段合同套成TCP合同，也不能以放宽Single或注入测试字段获得PASS。

## 实际修复及保留机制

两入口复用CapabilityDraft中的内置读取器目录解析：按observation、implementation、预期contract及无package/module选唯一已安装描述，使用其真实Version。DeviceBinding.Revision是设备修订，不冒充提供者版本。目录缺失/合同不匹配保持Provider=null，Build拒绝保存。扩展读取器继续走原package/version/module精确路径，不选择新版本代替原包。

不变更现场协议、TCP驱动、原生布局、业务/提供者版本、配置schema、SQL或原请求。该修复只改变桌面模型的构造；不可变介质仍以SourceCommit/逐文件哈希识别。双相机隔离、Epoch/Sequence、observe-only、Draft/Pending/Effective、保存CAS、受控激活及Unknown防重守卫均沿用原实现。

新增ReaderCatalogBindings原生夹具覆盖首配与真实运行参数模型：实际TCP目录版本及合成9.8.7均可解析；左右DeviceId/作业位/IP/端口/observe-only保留；目录缺失/错合同不可构造保存绑定；manual保持无字段；扩展原包可解析而其他版本不能替代；原候选字节未修改。模型验证不启动Application或连接设备。原首配字段、保存、回执、授权断言保留。

## 必要Windows开发路径验证

Windows原生HWND/Dispatcher，合成数据与loopback：

* `dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false`：PASS，0警告/错误。
* `dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false`：PASS，0警告/错误。
* `--startup-experience`：172项PASS，包含新目录回归、原TCP分帧/拒收/关闭、原首配字段校验、导航、授权失效、保存回读及回执丢失不重复保存。
* `--parameter-shell-polish`：PASS，原参数输入/管理模式/失权保存拒绝断言未放宽。

上述结果来自工作树重建。提交后同源冻结介质和夹具的独立回执另行附在本记录，不能用开发路径PASS代替CI。

## 运行和边界

先 `. ./scripts/project-environment.ps1`。从冻结介质执行仓库现有 `scripts/verify-commercial-wpf.ps1 -Media <D盘冻结目录> -Output <D盘新目录> -StartupExperience`，随后新Output执行 `-ParameterShellPolish`；默认读取介质SourceCommit的完整夹具，核验SHA256SUMS前后不变，RESULT.json分别记SourceCommit/FixtureSourceCommit及DLL哈希。无需修改现场路由、网卡、相机、有效配置或原业务库。

仅隔离Windows验证；真实相机/SDK/RCS、现场实体语义、目标工控机、生产资格和安装生命周期均未验。左右真实IP/端口/协议/line实体含义继续按原联调前置条件确认。未自动合并、发布、安装或请求生产设备。
