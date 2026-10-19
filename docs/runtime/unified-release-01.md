# main 统一发行构建 · UNIFIED-RELEASE-01

2026-10-20 从 main `c445b0ec32a3db44f00a1968f3c501aa36eb786f` 开工，继承 PR44/45/46 的组合源码。发行脚本冻结提交为 `07e0f7ab1e884d355c79ce87d6848ed507a1bf27`，不是 main 或 PR47 后续组合。开放 PR47 的商业化业务/UI/DB8 不在这个冻结来源里。工作包见 [范围裁决](../work-packages/WP-IPC-UNIFIED-RELEASE-01.md)。

**结果：发行流程已实现并提交；统一 Windows 安装媒介未产出。** 收到用户在“提交推送并合并全部PR”聊天中明确授权通知本任务“不测试、整理现有产物并提交推送”的收尾要求后，已停止在途构建与测试，没有继续构建、重新打包或新增验证。停止时首个 Sportmaster portable 正在生成 ZIP，未取得打包完成回执，也未完成统一发行核验；它是保留调查字节的中断产物，不可作为完整 ZIP/安装媒介分发使用。其余七个目标没有开始。当前没有 `RELEASE.json` 成功回执。

## 实现与复用

- `scripts/release-windows.py` 调用既有两个打包器，固定 SDK 10.0.402、Git commit/tree、PR44/45/46 祖先、锁文件和命令日志；三业务 win-x64 + portable、Supply/Inspection portable 共八个目标。后两者没有同等原生安装向导，不改标成 Windows 商业安装产品。
- `scripts/release_build.py` 统一 Git 导出、SourceLink/PathMap 隔离、导出管道排空和受限生成目录清理；ZIP 成员排序、时间和属性固定。长路径/SourceLink/桌面与 helper 公共 DLL 比较的机制继承 PR47 已存在的打包修正思路，仅处理发行层，没有引入 PR47 新业务。未执行第二次真实构建，**没有证明成套二进制或真实 ZIP 可重复字节相同**。
- `package-business.py` 的 Supply/Inspection 改用冻结导出、隔离编译输出和依赖资料；Supply 原封装契约仍由现有 seal.py 处理。`release_dependencies.py` 合并 app/desktop NuGet 资料、固定 UTF-8，并纠正“所有 Windows 使用均依赖 PowerShell”的过期描述。
- 发行核验代码检查源码/业务/版本/合同、逐文件 SHA、ZIP 成员/内容/CRC、安装入口、应用内清单及公共 DLL 同源；失败保留现场且不写成功回执。输出目录存在则拒绝覆盖。尚未执行完的路径见下表，不把代码存在当运行通过。

安装链源码核对：Setup.cmd → 原生 `--setup` → `DeploymentMedia.Inspect` → `ParcelStartupServices` 摘要/固定 helper/UAC → `WindowsDeploymentSetup.Install` 并排复制/ACL/保留种子/可选独立信任与静态扩展安装 → 最后部署登记 → 未提权启动。启动先执行共享身份门禁；Supply/Inspection 分别在配置与持久资源访问之前调用它。离线维护不构造业务/SDK，沿原账 CAS 保存 Pending，数据库检查走受保护临时副本。扩展随媒介携带、安装登记、配置绑定、生效和生产资格分别判定，不继承旧宿主签名资格。本次未更改上述业务机制。

## 已发生的必要验证及失败

下列结果全部发生在停止指令之前。原日志见 [证据目录](evidence/unified-release-01/README.md)。

|项目|实际结果与边界|
|---|---|
|main 基线 Application 验证工程 Release 构建|PASS，0 warnings / 0 errors，Windows 11 build 22631，SDK 10.0.402 / Runtime 10.0.13|
|现有发行指南测试|2/2 PASS；只涉及链接及冻结指南，非业务验收|
|新增发行门禁单测|5/5 PASS：内容缺失/额外/篡改、ZIP 内容/大小写重复、重封错误业务/源码、危险路径和归档时间稳定性。仅合成数据，不证明八个真实包通过|
|共享运行身份策略与首次准备委托|PASS：指定普通 SID、deny-only 管理员允许，错误 SID/启用管理员/系统服务拒绝；准备失败不继续。是合成 claim/委托测试，不是第二真实账户或 UAC 实验|
|启动/离线修复基线|PARTIAL / FAIL_ENVIRONMENT：已通过可选库存损坏仍可读、Production 缺能力拒绝、离线无数据读取、原结构保持、首个 Pending/原字节保存、CAS/占用/身份变更拒绝。已有 DB 检查在 Windows 维护权限门禁处抛 UnauthorizedAccessException，原完整套件未通过，不删除断言或绕过门禁|
|扩展构建工具测试|INTERRUPTED：已输出 1 FAIL、4 ok、1 ERROR，尚未产生完整诊断/汇总；停止后未继续分析或重跑，不能宣称扩展链通过|
|入口源码顺序测试|NOT_RUN_BY_USER_REQUEST：排在被中断的扩展测试后，尚未执行|
|八目标真实发行与统一核验|INTERRUPTED_BY_USER_REQUEST：首个 portable 编译输出已产生，ZIP 写入中断；没有完整成功回执，没有 Windows 媒介|
|重复真实构建 / 扩展签名装配 / 冻结媒介冒烟|NOT_RUN_BY_USER_REQUEST|
|Windows UAC/ACL/另一账户/受保护安装/中断恢复/桌面人工交互|NOT_RUN；当前非提权权限拒绝不代替这些验收|
|真实 SDK/协议/设备/现场 / 商用批准|BLOCKED / 未取得；D/RV 与旧发布证据不改|

## 后续运行方式（本轮未继续执行）

需 Windows x64、Python 3.12+、global.json 固定 SDK 10.0.402 和锁定依赖恢复能力。正常原生 Setup/启动/维护不要求 PowerShell；高级 .ps1 入口单独要求 PowerShell 7。从最终合并后最新源码重新冻结，不能把本次中断 ZIP 改名充当 PR47/main 的安装包。

```powershell
python scripts/release-windows.py --dotnet D:/tools/dotnet/dotnet.exe --output D:/releases/<全新发行目录>
python scripts/release-windows.py --verify-only --output D:/releases/<已完成发行目录>
```

构建只需普通开发账户；实际安装/UAC/ACL/运行账户后验另在获授权 Windows 测试环境执行。保留旧 e79a8bf 与 16ecb57 发布对象。未提供代码签名证书，不宣称 Authenticode 已签名或散列等同发布者信任。受限客户资料、生产配置、私钥和 SDK 不进入发行或此次提交。
