# Sportmaster 审查整改与定向性能分析

工作包：[WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01](../work-packages/WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01.md)。开工main `e0beb5d32c6a24a7799ba698731566bca6a6833a`，继承已合并PR57发行和PR58视觉实现。本页记录本轮实施和实测，不替换原审查及历史验收。

业务1.4.0、Application.Contracts1.10.0、Parcel DB8、配置schema3、Sportmaster默认supply-station；classic显式保留。没有业务协议、迁移SQL、运行资格或准入规则变更。原型D编号和独立审查D-01～03分开管理。

## 当前操作裁决

ParcelRepository缺省storageVersion=8，ParcelCli recover-legacy-receipt明确targetStorageVersion:8；旧API缺省5是兼容路径，不能当作当前CLI目标。新库不能交旧读者回滚。SQL1～7原字节不变；版本8由当前仓库迁移表定义，安装介质必须另核精确来源。

MachineRuntimePolicy.ValidateRuntimeIdentity要求指定SID；非提权UAC过滤令牌的deny-only管理员组可以通过，启用的管理员组、提升、服务和其他SID拒绝。runtime-identity-preflight-02中PR44尚开放/deny-only拒绝属于形成时记录；当前操作以实际政策及startup-deployment-02为准。源码判定与真实Windows令牌/ACL/安装验收分开。

## 本轮结果

实施完成后补入同版构建、原生WPF和R-02样本、证据索引与关闭条件。未执行不记为通过。

## 剩余发布阻塞

R-01需客户/上游确认同侧在途观察的准入归属和新实体规则，保留原审查失败，不用UI或合成样本替代。R-02无目标硬件、峰值节拍、历史规模与延迟预算，本轮仅软件分析和短时测量。真实SDK、设备、RCS合同/Unknown处置SOP和受控现场验证未取得；目标Windows版本上的完整安装/普通账户/双账户/重启/升级迁移/备份恢复需另验；签名、支持责任及商业资格未取得。正式商业发布仍NOT_READY。
