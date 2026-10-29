# CONTRACT-CONVERGENCE-02

基线：`e165e490`；2026-10-29。按用户本轮授权收敛现行合同，不新增功能。工作区为 D 盘独立 worktree，原工作区修改和未跟踪原稿保持不动；开始时无开放 PR。验证使用本机合成数据，CI 保持停用，不安装或连接现场设备。

## 候选与处置依据（修改前登记）

| 批次 | 候选 | 现行合同与生产入口判断 | 处置 |
|---|---|---|---|
| B1 | SM 装配中的 MvCodeReader、纯采图与 TCP 选择，以及 MvCodeReader 打包依赖 | AGENTS 的 MVS 软件识别规则、SM-BUILT-IN-ONLY-01；SM 的 Production 唯一相机入口为内置 `hikrobot-mvs-reader` | 删除 SM 注册/构造/打包分支，拒绝旧配置；保留其他业务 TCP、独立 SDK 包与其外部 ABI 测试 |
| B1 | `mvreader_runtime.py` 及打包测试 | 唯一调用者是 SM 发行脚本；现行 SM 无此原生依赖 | 一并删除；保留 MVS 依赖身份与缺失拒绝测试 |
| B2 | SM signed RCS 端到端测试与旧 MvCodeReader SM 流程 | SM 不再安装/启用独立扩展或用 MvCodeReader 识码；不能要求恢复这些入口来使测试通过 | 删除退休入口；现行 RCS 原请求/Unknown/心跳及 MVS SQLite 链保留并验证 |
| B2 | 同一 RCS 审计中可选 signed 分支 | 现行内置路径已经覆盖其请求编号/原件与备份不变量 | 合并为内置行为验证，保留全部相关行为断言 |
| B3 | 当前文档叠加纠偏与 AGENTS | 最新工作包优先；历史适用范围仅属于证据，不是保留代码的要求 | 原字节归档，当前入口写现状；不修改既有历史证据、D/RV |

## 保留与不确定范围

- 原请求冻结、Unknown、防重、原始回执及 DB/WAL 拒绝保全、管理授权、配置唯一账、资源排空/独占继续保留。
- DirectPlc、WcsToPlc、Supply、Inspection 及通用扩展机制不能因 SM 不用而删除。`retired` 身份表、数据根恢复/迁移和机级服务名称有当前责任，不按名字清理。
- 手工 Simulation 观察仍是显式验证入口；共享 TCP 的 loopback Simulation 测试保护启动屏障、同位防重、断线及资源释放，仍保留。TCP 不在 SM 可选目录/兼容声明中且不能进入 SM Production。独立 SDK 绑定及其 ABI/参数不属于本轮自有旧格式兼容删除范围。
- 未证明等价的测试不合并；验证覆盖范围和未确定项在运行报告逐项说明，不宣称所有源码逐行认证。

## 验证与计数

每批先构建，再执行受影响行为检查；生产组合构建与共享其他业务组合分别检查。测试工程是自定义控制台验证，报告分别计静态检查调用点和实际执行 PASS 数，不把断言数称为独立测试用例数。源码计数限定受 Git 跟踪的 src/tests/scripts 文本代码，排除历史证据和生成目录。实际结果见 [运行报告](../runtime/contract-convergence-02.md)。
