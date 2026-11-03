# Windows首次启动自动部署 · WINDOWS-FIRST-RUN-01

2026-10-19用户针对Windows缺少机器策略及UAC deny-only管理员被拒绝的问题明确要求修复。基线为main `524916b2088f267175bec46daf01e6df020932b7`，无开放PR；使用独立工作树，不触碰主目录未跟踪客户资料。继承F-01～12、REQ/BR/CAPI/D/RV及公开范围。本包的新通用安装实现可公开；不上传客户原稿、SDK、凭据或生产数据。

## 已确认需求和取舍

软件负责创建数据目录和部署配置，用户无需编写JSON、创建目录、修改ACL或专门新增普通账户。沿现有MachineRuntimePolicy实现首次启动准备，而非删除机级政策或新增每用户资源锁。相比仅增加手工安装脚本，此方案覆盖直接EXE、双击CMD与脚本；相比取消保护，保留跨会话资源所有权和维护边界。现有对话中的自动安装/初始化方案及用户“修复这个问题”构成本包实施授权。

首次启动：只读取得原进程用户SID并检查未提权/非服务账户；仅策略不存在时由固定安装helper请求一次UAC。helper只创建固定ProgramData/ImporterPc下的策略、租约目录及ACL，不接受任意目标目录、不加载扩展、不启动业务。UAC使用另一管理员凭据也绑定原用户SID。helper退出后原未提权进程重新执行完整策略校验，再进入既有数据锁/配置/SQLite初始化。后续启动不提权。默认数据根沿既有LocalApplicationData/ImporterPc/data；显式旧根不替换。

允许指定账户的deny-only管理员组身份；启用的管理员组、SYSTEM/服务账户及不匹配SID继续拒绝日常运行。此项明确替代原SID前置工作包对deny-only的限制，不倒写旧测试结论。直接API的RequireRuntimeIdentity与StartAsync仍是只读门禁，不让基座业务入口隐式提权。

已有策略、已有租约或扩展材料不覆盖/重绑；畸形/不安全策略失败并保留。新建目录在创建时带受保护ACL；通过保护目录内的独占安装锁串行化并发安装，先落盘临时策略，再无覆盖原子发布。中断临时文件保留；无最终策略且无租约/扩展责任的受保护安装骨架可重试。runtime-check/Diagnose保持只读，Run/Console/直接EXE执行准备。双击application.cmd直接启动自包含桌面，不依赖PowerShell；显式维护命令继续需要PowerShell7。

## 验证与边界

先跑既有脚本基线，再新增首次准备顺序、已有策略不提权、取消/失败/读回失败、原SID传递和deny-only允许/启用管理员拒绝回归。验证三个Application装配、Sportmaster WPF交叉构建及固定源码portable/win-x64媒介的内容与portable启动冒烟。Windows原生UAC、ACL、并发安装、双击与实际数据库不变性须分别留NOT_RUN；真实SDK/设备/合同/现场BLOCKED，Production准入不变。旧源码/候选、原失败及D/RV不变。
