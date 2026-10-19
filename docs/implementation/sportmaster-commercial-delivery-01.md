# Sportmaster 商业交付增量实现路径

本包设计先提交 `7b631b0`，起点 main `939462bee40c9eebe8eb2aa60860217afd95fcdd`；正常合入 PR55 的 `f3059d4` 后保留原回执/manifest 对象，唯一 INDEX 冲突保留新任务入口和旧下载身份。

|SCD|代码/交付路径|机制与限制|
|---|---|---|
|00|本工作包、设计、基线裁决、INDEX 与 pre-sportmaster 归档|先文档后代码，原 COMM/D/RV 不倒写|
|01/02|`src/Ipc.Desktop/StartupWorkflows.cs`|复用 StartupConfigurationDraft/IStartupMaintenance；五任务区保留原控件/草稿，主要动作按任务显示。44 DIP 控件、16 DIP 正文，技术散列展开，错误持续显示。检查与当前输入绑定；保存/修复/启动仍走原契约和守卫|
|01/02|同文件 Read/RequireMaintenance/RequireDirectories|重读/关闭/安装切换保护未保存输入；失败读取清除旧文档/实例，不跨根执行维护；受控恢复接受一次输入丢弃决定后读回新根，不重复询问。没有复制配置账、部署登记或生产默认|
|03|`ProductHelp.cs`/`ProductSupportWindow.cs` 与随包 `SPORTMASTER-DELIVERY.md`|任务搜索、明确无匹配态；原本地支持预览/确切摘要/时区/配额不变；交接、备份窗口、分诊、升级卸载和迁移表完整交付。支持联系人/承诺待真实合同填写|
|04|`scripts/release-windows.py` / `release_documents.py`|既有统一发行器新增 Sportmaster 双目标 profile；默认完整矩阵不变，复用原唯一打包器/库存/SourceLink/精确包闭包；指南随同版发布|
|04|`commercial_delivery.py` / `check-commercial-delivery.py`|固定十项 case、状态/源码/介质/实际执行回执/哈希路径检查；门禁与资格发放分离；当前 unsigned 候选强制拒绝生产裁决；不能由人工 JSON 替字节签名|
|05|`.github/workflows/sportmaster-commercial.yml`|PR 自动执行拒绝性政策测试、冻结双目标发布、核心/原生 WPF/卸载内核旅程；只读权限，禁止设备/共享机级安装；主分支 required 设置不由工作流文件自动产生|
|06/07|`verify-sportmaster-delivery.ps1` + 原三个 verify-commercial 脚本|同版介质、同版固定夹具、实际 DLL；每项立即记录，失败后继续其他独立旅程；WPF 原生日志失败也保留。管理员/重启/双账户和现场用任务手册逐项后验，当前 NOT_RUN/BLOCKED|
|08|逐项结果、evidence、manifest 与 PR56|最终冻结身份和必要验证由结果列出；合并必须核对服务端检查及 expected-head，不强推、不绕过|

`checkedInput`、页签与关闭确认仅为窗口展示状态，不是生产资格或新的配置权威。加密草稿仍由原 DPAPI/ACL/CAS 管理；原请求、DB8、业务1.4.0和契约1.10.0没有语义迁移。独立 Application、共享运行实例及原安装/helper/登记职责不变。DirectPlc/WcsToPlc 共享窗口可以继承展示整理，本包未授予其新的组合/真实接口资格，不重跑无关全量旅程。

第一次冻结 `0eea4b3` 的 WPF 夹具在异步加载/检查完成前操作，工程裁决真实 FAIL。诊断确认 Busy 尚未解除；保留失败源/介质/回执，不删旧断言，后继夹具增加加载完成、可编辑及焦点断言和命令完成等待，验证关闭取消及目录写入围栏。新增目录风险修正和最终 UI 必须在后继冻结介质重验。

后续门禁复核发现 Windows 旧代码页影响 JSON；`check-commercial-delivery.py` 固定 UTF-8，编排必须解析精确源码/库存匹配的实际裁决，不能把检查器崩溃的非零退出码当预期生产拒绝。旧编码回执按原字节保留。

GitHub Windows runner 在 `1f6e5b8` 的核心夹具发现健康探测外呼与维护保存相撞：原 CanConfigure 守卫正确拒绝，夹具错误假设认证后立即排空。最终沿现有 BeforeMonitorProbe 调度接缝控制一个真实在途探测，断言在途拒绝、不改加密字节/当前输入、实际排空后可保存，不降低守卫、不盲重试写入。`ParcelEditorDrafts.cs` 分别说明未暂停、未认证、外呼未排空原因，仍在同一业务所有者内校验。原 CI FAIL 及 checkout merge 源码单独归档，不挪用本机 PASS。
