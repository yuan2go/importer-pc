# D-DRIVE-INSTALL-01 证据

全部为开发机源代码构建/合成输入；不含生产数据、密码、SDK 或二进制。

- `preflight-before.log`：新增行为检查在原实现上实际失败，SYSTEM 拒绝访问目录被错误放行。
- `preflight-after.log`：修复后真实 ACL/只读路径及注入故障的 5 项行为检查。
- `installer-options.log`、`installer-change-guards.log`：原入口回归，后者 8 项，边界保留原输出。
- `matrix-authorization-failed.log`：真实提权交接失败，未取得受信 helper；无矩阵安装结果。
- `RESULT.json`：本轮验证状态、NOT_RUN 与未改变生产安装边界；不将软件注入的 1783 当现场根因。

C/D、D/D 的真实服务安装验收仍待真实管理员会话。不得将本机既有 D 盘服务的历史 RUNNING 或旧安装 PASS 转记到本提交。
