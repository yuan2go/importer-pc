> 2026-10-29 WT001续修：当前Supply 6的完整原始回执预检已前移到写连接与配置激活之前；本机当前合同57条PASS，Product本机验证受存储策略限制。范围、真实崩溃WAL及CI入口见[本轮记录](integration-20261028-supply-preflight.md)。下文原实施记录保留形成时来源。

# 当前版本与从零安装规则

工作包：[FRESH-BASELINE-01](../work-packages/WP-IPC-FRESH-BASELINE-01.md)。这是开发中的第一版，没有外部旧版用户；只支持当前版本从零安装、从零建库。任何非当前格式都明确拒绝，请使用全新数据目录。不迁移旧库、旧配置、账号或草稿，不提供旧入口桥接。原实施报告及K01～K15历史结论见[原件归档](../archive/pre-fresh-baseline-01/README.md)，冻结证据不改写。

当前格式、发布身份和外部兼容见[版本规则](version-compatibility-01.md)；安装见[Windows交付](windows-application-delivery.md)，首次操作见[运行入门](getting-started.md)。历史安装包及GitHub Releases保留，当前工具不重验历史介质。

## 必须保留的当前保证

- schema3显式拓扑、左右位隔离；四类参数共用唯一候选、CAS、Pending/Effective；当前启动失败修复与受控回退继续保留。
- Unknown固定原请求、目标、合同和字节；不得自动重发、假查询或用新回执补造原响应。
- 当前原始回执核对、独立接收发生、hash/冲突检查、原始回包保全和进程崩溃恢复保持。Query只使用真实声明的能力，RCS没有权威Query。
- 完整备份、隔离恢复、机器身份、源退役、RCS编号退役和消费者围栏保持；资源租约、单实例、停止排空和受控重启保持。
- WAL、synchronous=FULL、严格发布字节/签名/依赖校验保持。当前组合PASS不能凭同版本或历史PASS推断。
- 同数据格式覆盖安装、同版修复、卸载/快捷方式/服务归属保持；程序修复不倒退业务账。
- RCS外部version1和响应字段、海康SDK版本/原生签名、Win10 22H2/Win11门禁、SIMULATION-v1、V2/V3界面基线与产品编译组合保留。

当前离线recover-receipt保留原库/WAL精确hash、新完整备份、原发生ID、原发布和原因校验，仅执行原合同支持的Query。当前库恢复完成也不代表外部事实已经清偿。软件暂停不是安全急停。

验证结果在工作包按批次与实际commit记录。构建、合成验证、Windows原生、系统安装、真实设备与现场资格分别说明，未运行不得记录PASS。
