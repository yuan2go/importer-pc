# UNIFIED-RELEASE-01 实际执行产物

本目录仅收录收到停止指令前已经产生的日志、候选身份资料与中断归档原字节。`.gitattributes` 对本目录关闭文本换行转换，保留 [停止回执](stop-receipt.json) 中的 SHA256。散列用于证据保管，不是新一轮验证或发布者信任证明。

- [原始构建记录](build-record.interrupted-original.json)保持中断时 BUILDING 状态，不倒写成成功或完整失败诊断；追加 stop-receipt.json 说明被用户要求停止。
- `sportmaster-portable-07e0f7ab1e88.zip.partial` 是 ZIP 写入期间中断的现有字节副本，4716149 bytes，SHA256 `E36245A874BD93E0EEC7755A23CDC63B7A8E468BFB40920040A965A57C3F9E08`。没有完成回执或 ZIP 完整性核验，**不得用作安装/部署/升级媒介**。原本地路径 `D:/releases/ipc-unified-20261020-07e0f7a/sportmaster-portable-07e0f7ab1e88.zip` 保留。
- `candidate-ASSEMBLY.json`、`candidate-DEPENDENCIES.json`、`candidate-SHA256SUMS` 是中断候选已有资料；不是统一发行成功清单，不授予任何 Windows 或扩展组合资格。
- `baseline-build.log`、`baseline-guides.log`、`release-gates-unit.log`、`runtime-identity.log` 分别保存已完成的定向结果。`baseline-startup-partial.log` 保留 Windows 非提权已有 DB 维护失败，`extension-tools-interrupted.log` 保留未汇总的 FAIL/ERROR。空的 unified-build-interrupted.log 也保留，不能补造未输出内容。

冻结来源为 `07e0f7ab1e884d355c79ce87d6848ed507a1bf27`，业务 1.3.3 / Application.Contracts 1.9.0，不是 PR47 业务 1.4.0/DB8。没有 Windows ZIP、完整八目标媒介或 RELEASE.json。后续重新授权构建须在最终源码用全新目录重新冻结，不能重标此文件。

逐项边界和操作说明见 [实际结果](../../unified-release-01.md)。本次没有继续运行测试、构建、重新打包或验证器；文档和manifest只是记录已有产物与提交字节。
