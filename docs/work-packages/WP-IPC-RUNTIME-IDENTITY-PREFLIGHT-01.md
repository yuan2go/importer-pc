# 运行账户前置门禁 · RUNTIME-IDENTITY-PREFLIGHT-01

2026-10-19用户授权：独立复核PR35未解决P1，在不破坏PR37成果的前提下修复Run、Console和相关启动入口，执行必要定向验证，提交开发分支及main PR。实施基线为最新main `be7956447054054dfdcfce725e8fbbbdfc87407a`，已包含PR37/38/39/40；不修改其他工作树或受限未跟踪资料。

继承framework/F-01～12、现有Windows部署策略、单一配置/数据库及原请求责任。PR35与PR37的application.ps1 Git blob均为`b8c33d111713f124a2eefb6882ae33857022dd43`。[P1原线程](https://github.com/yuan2go/importer-pc/pull/35#discussion_r4310270689)在开工核验时未解决；原审查和历史结果保留，尚无Windows实机复现。

采用既有MachineRuntimePolicy的只读策略/ACL/SID/租约目录校验作为唯一验证机制。脚本先调用runtime-check，Application构造入口与StartAsync在数据锁、配置持久化、数据库打开/迁移/恢复之前再次验证，防止直接EXE/API绕过脚本。日常运行只允许指定普通SID，拒绝管理员（包括UAC deny-only管理员组）及SYSTEM/本地服务/网络服务账户。安装与维护原权限入口保留，不把普通账户门禁全局套入维护分支，不新增提权或自动换账户机制。

不修改UI、业务流程、SQL/schema、历史请求、现有策略文件、SDK/设备或现行发布资产。源码修复不改变旧媒介；部署必须从新固定源码重新构建可信候选，旧包/旧hash不能改称已修复。现有业务/Contracts版本保持，不借本包重新发布或覆盖已签名扩展。

验证：先记录实际脚本分支RED，再运行相同用例GREEN；共享身份判定定向测试；三个Application装配构建、受影响客户端交叉构建和一个固定源码portable媒介启动冒烟。只验证受影响入口，不跑全量业务/恢复/负载套件。Windows身份/ACL/UAC/实际数据库不变性另列NOT_RUN，真实SDK/现场仍BLOCKED。D批准及RV状态不改。结果和命令见[修复证据](../runtime/runtime-identity-preflight-01.md)。
