# HARDENING-04 云端交付清单

发布前 main：`752d5a89a9bf2a5e54c2551a3af77b82e1bc1961`；[PR #3](https://github.com/yuan2go/importer-pc/pull/3)；日期：2026-10-16。

用户随后明确要求不测试、提交推送全部成果并合并现有 PR。本次没有运行测试、构建、启动冒烟、归档校验器或历史场景，均为 `NOT_RUN`。[原实施报告](hardening-04.md)、[原机器结果](hardening-04-checks.json)及下列副本中的 PASS/FAIL 属于上一任务，未在本次重新执行，也不倒写。

## 源码、报告与原始结果

原开发分支已包含四个提交：`72d66962b1fa069b160a4a2d8fdf798c9c588d0e`、`2619a7a2a485a8176ae15ea7ed6fe0a18d34d9f6`、`ca922564295c2cdcc3bfb7ef69e95528dfea60a4`、`fbfe7ba56e749006506fc82a7aeceee424c4a778`。本次补充交付清单和以下执行产物，不再修改业务实现。

- [发布包原校验结果](evidence/hardening-04/hardening04-package-verification.json)、[初次冒烟结果](evidence/hardening-04/hardening04-smoke.json)、[最终冒烟结果](evidence/hardening-04/hardening04-final-smoke.json)从 `.artifacts` 原字节复制。
- [release.json](evidence/hardening-04/release.json)与[812 文件校验清单](evidence/hardening-04/release-SHA256SUMS.txt)从最终 ZIP 原字节提取。
- [ZIP 校验值文件](evidence/hardening-04/HARDENING-04-2619a7a-win-x64.zip.sha256)沿用原记录中的 SHA-256；本次未重新计算 ZIP 哈希、校验模块或运行 ZIP CRC。

## Windows 模拟包交付

最终原包：`.artifacts/HARDENING-04-2619a7a-win-x64.zip`；118942932 字节；原 SHA-256：`2d75536788c14d4df0325a6b6e2f2a74237ff9c9e5d348f28af0bb81235d2c88`。

包内绑定代码提交 `2619a7a2a485a8176ae15ea7ed6fe0a18d34d9f6`、源码指纹 `1e730f9a07560dc957a4b35a1f66161b40255f1b4b2266f9ac4477721aa13730`、模式 `Simulation`、目标 `win-x64`。不重新构建或打包；旧中间包与缓存保留本地，不冒充最终版本。

该 ZIP 及校验值文件通过[同仓库 HARDENING-04 Simulation 预发布](https://github.com/yuan2go/importer-pc/releases/tag/wp-ipc-runtime-hardening-04-simulation-20261016)交付，二进制不进入 Git 历史。清单在合并前形成，实时合并与附件状态以 PR/Release 为准；Release 标签包含实现及后续交付记录，不宣称在合并提交重新构建。RUNTIME-03 旧 Release 保持原样。

原报告、工作包中的“不合并/未上传 Release”保留形成时范围，后续发布按本清单及实际远端记录解释。Windows 桌面、维护脚本及现场验证仍未执行；真实 SDK/WCS、客户规则和实体/预留处置仍受阻；Production 为 NotReady，历史 RV-01～RV-17 仍 OPEN。

公开范围仅为通用参考实现、合成数据及对应产物。客户原稿和衍生资产、私密来源/凭据、生产数据、厂商 SDK、字体、运行数据库及构建缓存继续保留在公开交付之外。
