# PR24 独立复审归档与合并边界

2026-10-19（Asia/Shanghai）用户授权将本次独立复审报告和证据归档到同一PR24，确认head未变后合并main；不重复测试或打包。归档前再次确认PR head **f862c2ae2ad00ab79ceff5e2013c2a5593d05234**，main **b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**，PR OPEN/MERGEABLE。新增提交仅报告、证据和现行导航，业务/测试/配置/构建输入与被审head保持一致；随后按归档后的精确head合并。最终合并身份以[PR24](https://github.com/yuan2go/importer-pc/pull/24)及合并回执为准。

## 原件与结论

[独立复审原报告](original/REPORT.md) · [原结果JSON](original/review-result.json) · [原checksum清单](original/SHA256SUMS) · [归档原字节身份](archive-identity.json)。报告和JSON保持形成时字节，包括当时“只审查/未合并”的事实，不倒写成合并后的状态。

结论仅为：本次限定参考复审中，Standards和Spec均无确认剩余代码阻断，R03原schema1/2去重遮蔽反例闭合，R01～R05参考回归通过。既有3f6f762 CHANGES_REQUIRED、29项修复前失败、旧候选/包、原D/RV均保留，不把合并当成全包或生产验收通过。

## 已完成验证的原证据

以下均为上一独立审查已执行的记录，本归档/合并轮次没有重新测试、构建或打包。

- [库存矩阵原日志](original/inventory.log)：schema1/2/3 × Effective/Pending/未结FrozenConfiguration正反例及实际DirectPlc/CAS。
- [R01～R05原回归日志](original/review.log)：回执身份恢复、Attached原因隔离、混合消费者分派及维护期间发送围栏。
- [真实签名安装/恢复/卸载原日志](original/extensions.log)：schema1/2三类消费者六个卸载拒绝保留已安装包及原件；合法清偿后卸载。
- [独立字段探针原源码](original/probe/Program.cs) · [原输出与DB/config前后hash](original/probe-results.log)；[原构建日志](original/build.log)、[真实宿主构建日志](original/host-build.log)、[原样例构建日志](original/signed-build.log)、[原探针构建日志](original/probe-build.log)。
- [精确签名样例1.6.11身份](original/capabilities-1.6.11-independent.ipcx.identity.json) · [独立测试装载字节](original/signed-tests-overlay.json) · [宿主逐文件身份](host-custody.json)。仅记录此前合成验证样例，不是本轮新产品候选，不覆盖1.6.10或旧包。

原运行数据库、WAL、raw、配置、登记及非法消费者保留在本地不可变artifact `ipc-pr24-independent-rereview-f862c2a-20261018`。入库[逐文件原件索引](original-fixture-custody.json)，归档时不打开/整理/检查点数据库。签名样例及宿主二进制本地保留；不提交运行store、信任根、私钥、真实SDK、客户资料或生产数据。原SHA256SUMS包含本地保留的.ipcx条目，公开子集未附该二进制，见archive-identity.json；其余原件逐字节复制、分别核hash，不以缺失项冒充完整二进制发布。

## 继续保留的门禁

Windows实机安装/运行/WPF多窗/UAC/ACL、真实断电和生产长跑 **NOT_RUN**；真实SDK/设备/WCS/PLC专用合同、实体关联、复位/联锁/现场 **BLOCKED**。原审查01 PDF **BLOCKED_NOT_OBTAINED**，不以本报告或AUDIT02替代。Frozen新矩阵使用Prepared未结件，未穷举全部schema×Unknown/Conflict/图像/回执状态。完整产品打包、所有容量及配置激活断进程点未在独立复审中重复；历史267项资料错误未因此关闭。

合并不使Production自动Ready，不使7850fcc旧产品候选取得新源码资格。后续实际候选须按[Windows与现场清单](../../runtime/validation/architecture-quality-closure-01-windows.md)另行构建/核包/验收；本轮遵照用户要求不执行这些步骤。
