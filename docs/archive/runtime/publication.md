# RUNTIME-03 云端交付清单

日期：2026-10-16；发布前 main：`89089092ad8b7445071e0c12c9c558dc012a26e5`；[PR #2](https://github.com/yuan2go/importer-pc/pull/2)。

本次按用户要求提交、推送并合并源码、报告、文档及执行产物。没有重新运行测试、构建、启动冒烟、归档校验器或历史场景：本次全部 `NOT_RUN`。原[交付报告](delivery.md)、[checks.json](checks.json)及下列原始结果中的 PASS 均属于上一任务，不能解释成本次重新执行。交付报告中“本地保留/不合并”描述的是其形成时状态；实时发布状态以 PR 和 Release 记录为准。

## 提交内容与来源

- 实现提交：`04a3d46713b318c84999906ae2479b62e7d49cf0`。
- 既有报告提交：`dbe58f1c993f8113d9cd626195953052151524e5`。
- 本次增加云端交付清单和原始执行结果副本；不修改业务代码或倒写历史结果。
- [发布包原校验结果](evidence/package-verification.json)、[初次发布冒烟结果](evidence/smoke-results.json)、[最终发布冒烟结果](evidence/smoke-final-results.json)从 `.artifacts` 原字节复制。
- [release.json](evidence/release.json)与[逐文件清单](evidence/release-SHA256SUMS.txt)从最终 ZIP 原字节提取；[ZIP 校验值文件](evidence/WP-IPC-RUNTIME-03-win-x64.zip.sha256)采用原校验结果中的 SHA-256，本次未重新计算 ZIP 哈希或重跑 CRC。

## 发布包

[同仓库 Simulation 预发布](https://github.com/yuan2go/importer-pc/releases/tag/wp-ipc-runtime-03-simulation-20261016)承载最终 `.artifacts/WP-IPC-RUNTIME-03-win-x64.zip`，二进制不进入 Git 历史。发布状态及实际附件以该 Release 为准；本清单在合并前形成。

原记录大小：118902308 字节；原 SHA-256：`c185501702b51e70fd330e78aaacd2279cfb4f72e695b0ce3db3b6fad4825665`。包内绑定实现提交 `04a3d46713b318c84999906ae2479b62e7d49cf0`、源码指纹 `8ec828c921c9556ad3a50394d286d18c9614a120d9ad06b1c8a45770c3d01b69`，模式 `Simulation`，目标 `win-x64`；没有重新打包。发布标签包含此实现及后续交付记录，不宣称 ZIP 在合并提交上重新构建。

这是新通用规格和合成数据的参考产品。Windows 实际桌面、DPI/触控/听审、真实 SDK/WCS/现场及客户规则批准仍未完成；Production 保持 NotReady，RV-01～RV-17 仍 OPEN。

公开交付不包含客户原稿或衍生资产、私密来源/凭据、生产数据、厂商 SDK、字体、运行数据库、bin/obj 缓存或中间发布目录。`.artifacts` 中旧包和中间目录继续本地保留；最终 ZIP 通过 Release 单独交付。
