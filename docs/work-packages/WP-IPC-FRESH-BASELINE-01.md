# WP-IPC-FRESH-BASELINE-01

起点：main `6a55d9d537fb5365717b67c19d72374a4a52371b`。用户授权按B1→B2→B3清除C01～C20中的自有旧版本兼容；C21～C28保留。上一轮审计未在受跟踪文档中找到完整C编号表，按本次用户清单、先前审计和当前代码逐项复核。新worktree实施，不触碰其他worktree、未跟踪附件、本机安装或运行数据。

所有开发数据可放弃，不需要旧站接管。历史Releases、安装包、证据、回执、PASS/FAIL和合成旧库ZIP保持原字节。当前工具不重验历史介质。任意非当前格式明确拒绝，请使用全新数据目录。保留当前成功、拒绝且字节不变、折叠机制等价三类验证，不削弱Unknown、原件、WAL/FULL或资源守卫。

| 批次 | 范围 | 状态 |
|---|---|---|
| B1 | C14/C15/C18、smoke缺键和两处文案 | 实施中 |
| B2 | C05～C13/C17/C20 | 待实施 |
| B3 | C01～C04/C16 | 待实施；须旧链规范化schema与新基线等价、Parcel10/Supply6、恢复测试及干净起停冒烟全部满足后才合并 |

## B1源码核查

verify-operation-upgrade.py仍要求已退出的ForwardMigrationCommitted；station.ps1的Upgrade/Rollback调用退休命令，备份恢复独立可保留。smoke-application.py读MaintenanceLogin但当前schema3模板不产生该键；删除它不删除Login操作员能力。StrictSingleCode为当前明确策略，改为“严格单码校验”；管理服务只提示设置当前密码。

C18原指南中classic、旧库升级、MaintenanceLogin及历史资格重验指令与当前代码冲突。原字节保存在pre-fresh-baseline-01，当前指南和随包列表统一。B1无公共签名变更；不为文案改动重发旧包或改变数据库身份。后续版本/契约只在所属批次提升一次。

## 验证、合并及边界

各批在此追加实际源、定向测试、CI和合并SHA，不把历史PASS转记。系统安装、HKLM、SCM、已安装站点及真实设备操作本机NOT_RUN。源码回滚使用反向提交并重新CI；B3数据格式不可通过回退程序降级读取，使用全新目录。

B1本机验证：桌面验证工程Release/locked构建0警告0错误；随包文档3项测试通过；station.ps1语法检查通过；直接执行smoke可选能力实际表达式，三个当前Parcel模板均通过。首次辅助检查误遍历Inspection模板得到Wcs缺键，限定本脚本支持的三个Parcel模板后通过，未修改Inspection。无产品/安装/设备运行。CI及合并待记录。
