# AQA01 验证记录

本轮基线 main `0c5d7d25e572e80f23522c9ff7b562db920628a3`。执行结果和冻结产物身份在提交前补录；本页不继承原报告的PASS，也不将实现完成当成运行通过。

原只读审查：`D:\audits\importer-pc\AQA01-20261023-1622`，保持原字节和结论。整改逐次日志/命令/退出码：`D:\audits\importer-pc\AQA01-remediation-20261023`。材料仅本机合成与loopback，口令不输出；原失败日志不删除。

完整实施、兼容和Pending命令处置见[当前说明](../architecture-quality-remediation-01.md)。真实设备、厂商SDK和现场未操作；不修改HKLM政策或安装信任。

## 源码定向验证（2026-10-23）

环境：Windows 11 x64 10.0.22631，PowerShell7.6.5、Git2.56.0.windows.1、Python3.14.3、固定.NET SDK10.0.402 / runtime10.0.13。所有缓存、临时文件和验证数据在D盘；未更改机级策略、生产设备或保护信任。

以下命令统一先执行项目环境脚本，构建使用Release和RestoreLockedMode=true；测试入口为`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . <参数>`。

|记录名/参数|结果/退出码|解决的疑点|
|---|---|---|
|build-verification-09 / dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true|PASS / 0，无警告|真实运行时和既有UI投影编译|
|remediation-04 / --architecture-quality-remediation|PASS / 0|排队过期、实际投影、直接重启、旧回调/序号、双位、TCP运行/备份、管理退出/到期/改密码、六边界真实进程崩溃/同ID/原请求守卫及中断检查|
|application-architecture-03 / --application-architecture|PASS / 0|原挂起套件完整双位/三业务/可选能力/资源/迁移断言|
|resource-risks-02 / --resource-risks|PASS / 0|Windows机级设备租约、Stop超时保有、受控激活失败保旧、原raw恢复|
|configuration-confirmation-01 / --configuration-confirmation|PASS / 0|原配置CAS/hash/runtime及双窗守卫，确认保持暂停|
|receipt-custody-01 / --receipt-custody|PASS / 0|真实原raw冲断/缺失，Query只确定结果、原件恢复与再归档、不重发|
|ux-commercial-inputs-02 / --ux-commercial-inputs|PASS / 0|初始陈旧时间戳不推进序号、旧实体不回退显示、Unknown责任、设备/Revision重绑与输入/作业原子性|
|release-documents-01 / python -m unittest discover -s scripts -p test_release_documents.py -v|PASS / 0，2项|随包文档链接与缺失/越界拒绝|

早期失败保留：build-verification-01（新夹具遗漏ISyntheticObservationInput签名，退出1）；remediation-01（新夹具kind/admission参数顺序错误，dotnet退出-532462766）；application-architecture-01（其他旧Reconcile调用未授权）、-02（取消修复使所有权超时夹具提前结束），均保留退出/完整栈；ux-commercial-inputs-01在第二重绑保存断言失败，未输出原回执，不能倒推其唯一原因。随后保留原断言，补真实管理授权、有界/按用途的取消和finally释放，以及只对明确调用前拒绝的重试与实际回执日志。后续成功记录独立保存，不替换原失败。

## 冻结产物与边界

源码提交后使用原package-application.py冻结Git导出；选装、解包完整SHA、实际CLI/可选取图上传重启及必要Windows目标构建的结果在独立证据提交补录，冻结产物始终保留自身SourceCommit。本轮不发布/安装新介质，不把原SM1.11.1重测或历史安装PASS扩张到本分支。

真实SDK、真实WCS/PLC/称重/取图协议和现场为BLOCKED（没有相应设备/合同条件）；实际安装/UAC、受保护Store卸载和原生窗口人工验收为NOT_RUN。扩展离线读取直接验证了真实已运行TCP根及完整备份，Store链的适用性为源码证据，未做机级Store变更。目标构建与本机portable进程不等于现场验收。
