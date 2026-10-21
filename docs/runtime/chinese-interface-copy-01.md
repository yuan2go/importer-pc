# 界面中文文案增量

2026-10-21；基线 main `20251b4c10db76a37f4d204332e3e6cf68340429`，开工工作区干净、开放 PR 为零。用户要求页面提示全部中文。本增量复用 PARAM-UX-02 / PS-05 与现有作业、首配实现，只调整展示文案；历史原型中的双语标签由本轮中文要求替代，布局、字段归属和业务合同保持。

作业页将英文暂停提示及运行模式改为中文；波次、任务号、商品编码、返回数量不再附带英文技术键。参数页、作业记录和维护工具的固定提示、单位、常用状态和选项采用中文；首配及安装准备的运行模式选项复用同一展示转换器。

`ChineseInterfaceText` 只在桌面展示层转换已知状态和提示。下拉框仍绑定原始枚举或协议值，未将“仅观察”“触发关联”等显示文字写入配置。完整原件、报文、协议字段、地址、实际商品/任务编码、精确安装身份及外部来源原始诊断保留原值；这些属于数据和技术证据，不能翻译后冒充原件。没有修改驱动描述、签名包、数据库格式、请求内容、权限或恢复守卫。

## 运行与验证

沿用仓库固定的 .NET 10.0.402：

```powershell
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll <仓库绝对路径> --parameter-v3
```

原生夹具使用隔离目录、合成配置和模拟端口，不连接真实设备。本轮新增检查确认：中文暂停提示保留应用原始事实；真实下拉控件显示中文，切换后仍保存原协议值。沿已有参数 V3 入口核对草稿、保存、恢复、错误定位、敏感引用、对象诊断及作业任务字段。

本机 Windows 125% 最终结果：桌面及原生验证项目 Release 构建均通过，零警告/零错误；原生入口退出码为 0，[日志](evidence/chinese-interface-copy-01/WINDOWS-WPF.log)中有139条通过记录和32次截图。覆盖1366×768、1092×614和800×420逻辑客户区等既有检查；日志中的尺寸是原生窗口 backing surface，不冒充物理屏幕尺寸或目标工控机实测。已实际查看[作业页](evidence/chinese-interface-copy-01/work-assigned-1366.png)、[系统参数](evidence/chinese-interface-copy-01/parameters-system.png)和[已保存待重启](evidence/chinese-interface-copy-01/v3-saved-pending.png)，文案及相关布局正常；其余合成截图保留在本次本地验证目录。

首轮新增暂停断言发现夹具初始未暂停，断言失败保留；改为显式提交暂停并等待读回后核对，未删除断言或修改业务初始状态。第二轮原生中文选项显示已通过，但新增提交检查对原仅用于显示的敏感字段描述执行完整校验，得到 `Invalid parameter definition: masked; Invalid parameter definition: reference`；为新增选择/提交检查建立独立、合规的选项描述后通过，原敏感显示断言保持。

本轮未发布安装包，不覆盖原 1.7.0/1.6.1 的 SourceCommit 或验证结论。真实 SDK、调度接口、目标工控机及现场验收未执行；D/RV 与旧 HTML 证据不变。Impeccable Operate 的文案检查用于本轮原生界面；网页检测不能代替 WPF 验证。
