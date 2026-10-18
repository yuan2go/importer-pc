# PR31 独立复审归档与授权合并

2026-10-19用户授权将本次独立复审报告和证据归档到同一[PR #31](https://github.com/yuan2go/importer-pc/pull/31)，确认head未变后合并main，不重复测试或打包。归档前远端head仍为 **f068156ed9d4a0ffe4feee94b5c11e966aaa366b**，main为 **baac260214f910dca9ae4628ba6449b25188e2ac**。本轮只追加文档/证据；业务、测试、配置、构建脚本和依赖与被审head相同。先将归档提交推送到同一PR，再使用归档后的精确head正常合并；实际最终head、mergeCommit及mergedAt以PR31/GitHub记录为准，不预填未知提交。

## 原报告与固定身份

[原报告](original/REPORT.md) · [原结果JSON](original/review-result.json) · [原checksum](original/SHA256SUMS) · [归档身份与逐文件比较](archive-identity.json) · [源码/原FAIL保有证明](original/source-custody.json)。原报告形成时“PR OPEN/只审查/未合并”及原PASS/FAIL保持原字节，不倒写。

限定结论：DB4升级前责任判定、schema1/2实际main冲突、Pending激活屏障及architecture-quality原失败闭环范围内，Standards/Spec均无剩余P1/P2阻断。原审查78298b5 CHANGES_REQUIRED、architecture-quality旧FAIL、新增RED和历史环境/夹具失败继续见既有归档及[实施证据](../../runtime/evidence/pr26-recovery-closure/README.md)。旧返回回执未记录的观测缺口也保留。

## 已执行的独立证据（本轮不重跑）

- [DB4/main/Pending定向日志](original/upgrade-conflict-closure.log)、[真实PR24 DB4副本](original/historical-db4-upgrade.log)。
- [architecture-quality](original/architecture-quality.log)、[旧账确认/CAS/中断](original/configuration-confirmation.log)、[回执资料保有](original/receipt-custody.log)。
- [实际旧DB4归档资料迁移](original/prior-custody-migration.log)、[匹配旧发布DB3 Query/断进程](original/legacy-custody-upgrade.log)、[原构建日志](original/build.log)，对应*.exit.json记录原退出码和宿主DLL身份。
- [独立负向探针源码](original/negative-probe/Program.cs)、[工程](original/negative-probe/Probe.csproj)、[原结果](original/negative-probe/results.json)、[原构建日志](original/negative-probe/build.log)：缺Query原件、hash不符、孤儿未结均保持DB4、外部Query=0、原发生保留。

完整SQLite/WAL/raw/图像/配置、源码快照及构建原件在本地artifact `ipc-pr31-independent-review-f068156-20261019`保留。[运行原件逐文件索引](original-fixture-custody.json)通过静态字节读取记录hash，本轮不打开数据库、不checkpoint、不重打候选。

两份含本机绝对路径的元数据以显式[身份映射副本](public/identity.public.json)/[运行根映射副本](public/runtime-custody.public.json)公开，原JSON保持本地原字节及SHA256；local-artifact:后为本地artifact标识和相对路径。原SHA256SUMS仍含这两份本地保留原件条目，公开映射不冒充原件。其余归档原件逐字节复制。只公开通用/合成审查证据，不提交运行库、凭据、信任根、SDK或客户资料，不改变仓库可见性。

## 保留的边界

本次归档/合并轮次测试、构建、打包均 **NOT_RUN_BY_USER_REQUEST**。Windows安装/WPF/HID/UAC/ACL/Windows卸载 **NOT_RUN**；真实SDK、设备、WCS/PLC/身份协议、实体关联、复位/联锁/现场及Production资格 **BLOCKED**。新完整候选/签名组合/真实卸载、Supply/Inspection完整验收、长期断电/坏介质及完整旧站迁移未因此通过。原PDF01仍未取得；旧267项资料问题、D/RV和原件/历史失败均不关闭。

并行PR27/29/30/32未纳入本次合并授权，尤其恢复相关重叠需其后按实际main/head集成核验。本次不代合并其他PR，也不修改其工作树。
