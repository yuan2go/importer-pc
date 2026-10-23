# WP-IPC-ARCHITECTURE-QUALITY-REMEDIATION-01

基线 main `0c5d7d25e572e80f23522c9ff7b562db920628a3`，2026-10-23复核无开放PR。用户授权整改独立只读报告的六项问题，独立开发分支提交并创建main PR，不合并、不操作生产设备。原报告及复现材料保留在D盘独立审查目录，不改写原失败与历史事实。

|问题|根因及本轮范围|可复核验收|
|---|---|---|
|AQA01-F01 P1|有效会话排队过期只审计，旧格口仍可见；使用现有InputObserved账|超时新输入持久拒绝、无新请求，上一终结格口不再为当前；旧会话/重复不误伤、双位独立、重启保持|
|AQA01-F02 P2|命令身份与局部业务事实双写|局部变更和命令回执同事务；长调用先持久命令准入/责任，无长网络事务；崩溃后同ID同内容返回原回执，异内容拒绝，原请求不盲重发|
|AQA01-F03 P2|离线引用读取未识别内置tcp-line|运行、Effective/Pending/Frozen及完整备份离线识别一致；未知/损坏保持拒绝|
|AQA01-F04 P2|普通Inputs分支保留reader参数|普通快照、历史、Operation投影守管理模式；原存储与授权读原件不变|
|AQA01-F05 P2|测试沿用旧权限/生命周期|真实管理授权、取消和finally释放，Windows租约不自占；实际包可选流程重启核对通过|
|AQA01-F06 P3|当前指引混用历史版本/保存/缺省|编码前归档并修正相关当前入口，构建随包说明核对；历史SourceCommit、介质和验收不倒写|

复用三独立Application及ParcelFlow、唯一配置账、现有命令表、真实SQLite和真实管理接口。无新万能总线或第二框架。首次整理已归档README/INDEX/入门/部署/Application/ABI/扩展指南原件及哈希；四类职责、原请求/图像/回执责任及D/RV不变。运行材料仅合成数据和loopback，必要构建/测试/发布启动证据单独记录。Windows本机合成、真实SDK/协议及现场验收分开；不安装/修改机级策略或生产数据。DirectPlc完整UI延期不纳入本包。

## 本轮收口

六项均已按范围整改；F01包含实际投影及直接重启验证，F02包含六个持久化边界子进程崩溃与中断检查。完整实现见[当前说明](../runtime/architecture-quality-remediation-01.md)，命令、退出码、冻结SHA、原失败及NOT_RUN/BLOCKED见[验证记录](../runtime/validation/architecture-quality-remediation-01.md)。实现冻结7024f1e，测试驱动fe8d9dd；三种portable及Sportmaster win-x64同源，未发布安装包。本包以main PR供复核，不合并；原D/RV与历史资格保持。
