# 日常参数页登录会话时长修复

后续交付：修复PR76已合并，实际main源`92586bf44cde7efb6cec1538d7b9654ee518c0fb`。最终发布DLL聚焦24项及Windows实际输入、安装准备/取消结果和1.6.1下载见[发布记录](publication-login-session-20261020.md)。本段为冻结源码后的补记，EXE随包指南保留92586bf原字节。

本增量基于 main `837370e3c21a4b83ca0f20dc9a606e760ba9acac`，处理[PR74原审查](https://github.com/yuan2go/importer-pc/pull/74#discussion_r4326168214)。原1.6.0发布记录保留当时“已知未修复”的事实；当前修复、必要验证与新安装包来源在本页补记，不倒写旧包及原验收结果。

## 裁决与配置边界

schema-3 日常编辑器由 `ConfigurationRoles` 的“登录”角色选择决定操作员登录绑定，组件库存上的 `Role/Bound` 是不同表示，不能用首配专用标记推断日常适用性。日常 `ParameterEditorModel` 只读引用原角色选择；首配仍读取其原 `operator-login` 草稿。字段继续双向绑定原 `ConfigurationPolicyDraft.SessionLifetimeMs`，完整候选、配置CAS、维护授权、暂停/原责任守卫、回执核对和受控重启保持同一路径。

绑定时在“系统 / 数据与记录”显示原值，解绑时隐藏但保留原始输入，重新绑定使用同一个控件。只绑定维护认证不代表操作员登录。若该策略已有校验错误，即使解绑也保持字段与错误可定位，允许纠正完整候选，不能因字段隐藏吞掉错误或重置值。校验范围仍100～86400000 ms，保存只进入Pending，Effective待受控重启。

## 必要验证与发布

聚焦 `--login-session` 使用真实WPF/Dispatcher及隔离合成端口：24项通过，涵盖绑定/解绑/再绑定、维护角色隔离、首配兼容、搜索定位、原值/无效原文/边界、同候选、刷新保留、800×420小窗口可达性、未授权拒绝、授权保存单修订与Effective不变。修复前的首版聚焦夹具在缺失字段处失败，原日志保留。测试开发中一次把规范化完整候选与原种子JSON作逐字节比较而失败；已改为与修改前的同一完整候选比较，保持全部其他字段逐字节断言，原失败不删除。

Sportmaster测试宿主及DirectPlc/WcsToPlc受影响桌面构建均0警告、0错误。版本/兼容17项、随包文档2项通过。补丁声明为Sportmaster **1.6.1**、DirectPlc/WcsToPlc **1.4.4**；公共Application.Contracts仍1.13.0，数据库、SDK/外部协议及精确扩展版本不变。本次未新建或签名扩展闭包，未发布另外两种业务安装包。

[聚焦原日志](evidence/login-session-lifetime-01/focused-pass.log)、[原生WPF图片](evidence/login-session-lifetime-01/daily-session-lifetime.png)、[测试和构建边界](evidence/login-session-lifetime-01/VERIFICATION.json)保留方法和文件身份。开发阶段图片/程序集属于该次工作树构建，合并后发布程序集的验证和Windows实际输入将另记，不提前声称已验证未生成的安装包。不重复条码、RCS或无关UI套件。

复现：`dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true`，随后 `dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --login-session`。`--interactive`在聚焦验证完成后保留原生窗口以进行实际键鼠输入，端口和数据仍为隔离合成夹具。

真实SDK/设备/RCS、目标工控机、其他Windows/DPI/触摸与现场验收仍待验。原型、WPF合成、Windows实际输入、安装准备与完整安装/UAC/升级分别记录，不把局部PASS扩张为生产资格。
