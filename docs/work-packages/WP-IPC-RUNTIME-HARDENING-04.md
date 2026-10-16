# WP-IPC-RUNTIME-HARDENING-04

实施基准：main `752d5a89a9bf2a5e54c2551a3af77b82e1bc1961`（PR #2）；2026-10-16核验无开放PR。原工作区未跟踪客户资料保持原样；独立worktree、开发分支 `codex/wp-ipc-runtime-hardening-04`。

继承现有F-02 C#参考产品。当前授权为工程加固及模拟验证，后续关联 WP-IPC-INTEGRATION-BOUNDARY-05、WP-IPC-WINDOWS-VALIDATION-06。D登记、17项RV和旧验收保持历史状态，不复制决策包。

## 设计与实施步骤

- [x] 在实际SQLite、独立loopback接收账上跑必要基线；增加可复现过载+Prepared互锁、标记复活、旧活动报警遗漏的回归。
- [x] `StationApplication`保持过载发送/准入守卫；为有持久未交付证明的Prepared增加维护处置命令及投影。取消、T2领取、发送授权在同一协调器串行裁决，发送授权持久记录交付边界；Unknown与已交付不本地取消。原身份/报文/绑定/尝试/审计保留；本地终结另置待处置阻断，不释放既有预留、不允许实体离场/新入场。
- [x] `ObservationModule`在过载后关闭新输入，已接纳事件继续逐条持久处理；恢复与源回调互斥，排空后记录维护证据并确认当前标记。故障确认不消除其它阻断；重启保留未确认过载，新一轮过载保留新证据。
- [x] `SupplyStore`活动报警及计数独立于历史分页；按报警ID已读，恢复只匹配同一对象/来源/代次；WPF显示活动/历史及请求阻断和允许的下一步。
- [x] 恢复稳定后将SIMULATION-v1编解码与模拟策略收敛为显式模拟边界。历史payload/response JSON及schema1迁移字节不变，不设计真实WCS合同。
- [x] 定向回归覆盖身份过期、取消/交付竞争、迟到/重复回执、双通道；完整受影响验证、构建及发布核心冒烟。当前macOS，无已配置Windows执行环境，桌面/维护运行记NOT_RUN。
- [x] 更新必要运行/恢复说明、本包结果及manifest，提交开发分支并创建main PR，不合并。

替代方案已排除：忽略Overload继续发送会绕过输入缺失证据；允许Prepared直接清库取消会丢失原意图；放大报警LIMIT仍不能保证旧活动报警可见。采用受控本地终结、独立故障确认及活动查询，不改变生产规则。

实际结果与剩余外部/Windows边界见 [HARDENING-04结果](../runtime/hardening-04.md)。

实际结果与剩余外部/Windows边界见 [HARDENING-04结果](../runtime/hardening-04.md)。
