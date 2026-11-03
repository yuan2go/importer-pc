# IE01 CI与main保护

当前PR/main工作流：[industrial-core.yml](../../.github/workflows/industrial-core.yml)。它执行locked restore、25项标准测试及逐项身份核对、四个托管产品程序集的子进程覆盖率、41文件格式、文档、被动性能与发布策略测试；三个桌面装配分别构建。真实SDK/设备、UAC/SCM和最终安装生命周期不由这些工程检查代替。

## 已核对，尚未应用的仓库设置

2026-11-03只读GitHub接口显示：`yuan2go/importer-pc` 的main为`protected=false`，没有必需检查。源`ac0fd43853eefc2484e710551d965fc03e815b79`的四个成功check-runs均来自GitHub Actions，App ID为15368。可审查的最小集合见[main-required-checks.json](../../.github/main-required-checks.json)；此文件存在不表示GitHub设置已生效。

仓库所有者应用时：

1. 在仓库Settings的Branches/Rules中打开main的保护规则；没有规则时新建main规则。
2. 启用必需状态检查和合并前与main保持最新。添加配置中的四个检查，并将来源绑定GitHub Actions。
3. 保留已有必需检查、审阅要求、限制和绕过规则；不要用这份最小集合覆盖新增的其他规则。JSON对应状态检查部分，不是完整保护设置。
4. 保存后重新读取main和保护规则，核对strict、检查名和提供者；再核对待合并PR中的同名结果。未经读回不能记为完成。

API语义见[GitHub官方保护规则说明](https://docs.github.com/en/rest/branches/branch-protection)。首次核对时main无现有保护；应用前仍须刷新现状，防止覆盖并行设置。

## 接口与自动化限制

连接器可见当前账号为仓库管理员，但托管集成不含Administration接口能力，读取保护规则返回403 `Resource not accessible by integration`。读取缓存Git认证并调用API的备用命令被自动审批以`blocked by policy`拒绝，命令未执行、未读取凭据；没有改用其他命令读取它。computer-use指南禁止通过界面修改应用安全/隐私设置，本任务将分支保护归入该限制，因此没有进行界面修改。

因此这项仍为**待仓库所有者应用**。CI已自动执行与“main已强制必需检查”是不同事实；本任务未声称后者完成，也未自行合并PR或绕过保护。
