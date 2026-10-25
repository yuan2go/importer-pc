# 独立延后配置续修证据

2026-10-26；基线main `3a5b1d13`，新增源码和原生测试在本目录同次提交。全部输入为独立合成工位、loopback TCP/HTTP和临时D盘数据；没有现场设备、原业务库或客户资料。文件大小与SHA256见[FILES.json](FILES.json)，运行方法与边界见[报告](../../validation/deferred-heartbeat-01.md)。

| 原件 | 结果与含义 |
|---|---|
| `before-fix.log` | FAIL：原main加新增回归，左相机延后时编辑器通过、真实预检失败；实际复现P2。 |
| `matrix-first.log` | 环境拒绝：修复预检后真实工厂因本机未安装运行策略拒绝获取设备租约。保留原拒绝，不改变本机保护配置。 |
| `matrix-third.log` | FAIL：补齐延后RCS后，旧不完整zone-id被用于范围注册而拒绝启动；本轮追加修复依据。 |
| `matrix-fourth.log` | 夹具时序FAIL：前五组恢复通过，第六组保存遇到既有健康读取在途守卫。夹具改为等待Configure，仅重试原无副作用的忙态拒绝；不改生产守卫或删除断言。 |
| `matrix-final.log` | PASS：七组延后/重连/Pending/受控恢复、同码独立请求及Unknown原身份保护。Windows端口装配为显式测试夹具；CI Linux走真实工厂。 |
| `parameter-guidance-startup.log`、`rcs-heartbeat.log` | PASS：原首次配置、完整候选/日常Pending及RCS心跳周期、停止、失败、编号隔离等回归。 |
| `parameter-refactor.log` | 静态检查通过后因缺少本机运行策略中止，整项未通过，不以此前PASS替代。CI原生参数/首配另行验证。 |
| `WINDOWS-WPF.log` | PASS：真实WPF控件七组独立延后与离线预检、重新启用缺编码拒绝，以及原全延后作业窗口、准入阻断、全屏和键盘回归。Windows 125% DPI；不等于物理100%DPI/触控验收。 |
| `deferred-left-active-rcs.png`、`deferred-workstation.png` | 同一原生专项截图，已视觉核查；前者为左延后时的已配置RCS页，后者为全延后的实际作业窗口。 |
| `build-final.log`、`build-wpf-accepted.log` | Application验证与原生WPF验证工程构建：0警告、0错误。 |

早期新夹具编译错误及原生填写遗漏/断言层级错误的日志仍保留在D盘`.artifacts/deferred-heartbeat-01`和其各次临时证据目录，不改写为PASS。原生恢复缺编码断言最终落到真实离线预检（普通字段schema允许先保留空编码），依然要求拒绝启动候选；不是取消必要检查。

后续冻结构建与CI按各自SourceCommit/HEAD记录，不扩张本次工作树回归至旧介质、真实设备或现场资格。原D/RV不改。
