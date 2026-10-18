# WP-IPC-APPLICATION-CAPABILITY-CLOSURE-19

来源：2026-10-18用户实施授权A～F及验证/交付要求。开工main08b8067aa6cba6a148252296c7a5b18a27b3be65，PR18已合并，无开放PR；开发工作树application-capability-19，主目录未跟踪资料原状保留。

[唯一现行设计](../design/application-capability-19.md)落实用户固定边界；原REQ/BR/CAPI/D/RV、历史报告/SQL/checksum/包身份不改。全部用户原要求保留于任务会话；此文件是执行映射，不替代或降低原目标。

## 实施计划（writing-plans；本会话顺序执行）

- [ ] AC19-A：Platform公共能力及Application.Contracts业务协议，FlowDefinition改为独立Application决策；修改三个Application.cs、ParcelRuntime/Stages，保留事务/资源基础设施。
- [ ] AC19-B/C：公共会话有效期、原身份、维护授权；WeightPolicy等待期限、统一守卫、超时处置；ParcelRepository.Commit将测量与关联同事务。验证通过三个真实Create入口。
- [ ] AC19-D/F：扫码关联采图、两类上传独立状态、有限缓存/释放重启；外呼释放锁并跟踪在途，Pause/快照不会等待网络。复验ContinuePrepared和Unknown不重发。
- [ ] AC19-E：现有签名包种类/精确加载/资源租约新增认证相机上传；现有配置权威支持实例启用/绑定；客户端结构化能力/schema编辑、安装、会话与独立状态/处置，不交JSON调试替代品。
- [ ] AC19-V：tests/Ipc.Application.Verification新增19风险选择器，17/18受影响回归、协议样例与签名包、Supply/Inspection受影响回归；三WPF构建、三portable ZIP解压与独立HTTP进程冒烟。实际失败保留。
- [ ] AC19-DELIVERY：现行指南/manifest/能力矩阵/运行与剩余接入清单、源SHA及日志hash；提交开发分支并创建main PR，不合并。

验证命令：固定dotnet10.0.402构建tests/Ipc.Application.Verification，运行新增--capability-closure及原默认/--site-capabilities；scripts/package-application.py与smoke-application.py沿原入口。Windows实际运行NOT_RUN，真实SDK/设备/现场BLOCKED。
